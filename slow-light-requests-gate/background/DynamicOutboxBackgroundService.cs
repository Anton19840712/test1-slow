using lazy_light_requests_gate.middleware;
using lazy_light_requests_gate.processing;
using lazy_light_requests_gate.repositories;
using MongoDB.Driver;
using Npgsql;

namespace lazy_light_requests_gate.background
{
	public class DynamicOutboxBackgroundService : BackgroundService
	{
		private readonly IServiceScopeFactory _serviceScopeFactory;
		private readonly IRabbitMqService _rabbitMqService;
		private readonly ILogger<DynamicOutboxBackgroundService> _logger;
		private readonly TimeSpan _delay = TimeSpan.FromSeconds(5);

		// Добавляем параллельную обработку
		private readonly SemaphoreSlim _semaphore;
		private readonly int _maxConcurrency = Environment.ProcessorCount * 2;

		// Для отслеживания Change Stream задач
		private CancellationTokenSource? _changeStreamCts;
		private CancellationTokenSource? _postgresListenCts;

		public DynamicOutboxBackgroundService(
			IServiceScopeFactory serviceScopeFactory,
			IRabbitMqService rabbitMqService,
			ILogger<DynamicOutboxBackgroundService> logger)
		{
			_serviceScopeFactory = serviceScopeFactory;
			_rabbitMqService = rabbitMqService;
			_logger = logger;
			_semaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("=== DYNAMIC OUTBOX BACKGROUND SERVICE STARTED ===");
			_logger.LogInformation("Using smart database detection mode - will check database type before each operation");

			// Основной цикл - проверяет тип БД перед каждой операцией
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await ProcessOutboxForCurrentDatabase(stoppingToken);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error in main processing loop");
				}

				// Уменьшаем интервал для MongoDB, так как нет Change Streams
				var delay = await GetOptimalDelay();
				await Task.Delay(delay, stoppingToken);
			}
		}

		private async Task<TimeSpan> GetOptimalDelay()
		{
			try
			{
				using var scope = _serviceScopeFactory.CreateScope();
				var factory = scope.ServiceProvider.GetRequiredService<IMessageProcessingServiceFactory>();
				var currentDatabase = factory.GetCurrentDatabaseType();

				// Для PostgreSQL можно реже проверять, так как есть LISTEN/NOTIFY
				// Для MongoDB нужно чаще, так как нет Change Streams
				return currentDatabase == "postgres" ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(5);
			}
			catch
			{
				return _delay; // fallback
			}
		}

		private async Task ProcessOutboxForCurrentDatabase(CancellationToken stoppingToken)
		{
			try
			{
				using var scope = _serviceScopeFactory.CreateScope();
				var factory = scope.ServiceProvider.GetRequiredService<IMessageProcessingServiceFactory>();
				var currentDatabase = factory.GetCurrentDatabaseType();

				_logger.LogDebug("Processing outbox for database: {Database}", currentDatabase);

				if (currentDatabase == "postgres")
				{
					await ProcessPostgresOutbox(scope, stoppingToken);
				}
				else if (currentDatabase == "mongo")
				{
					await ProcessMongoOutbox(scope, stoppingToken);
				}
				else
				{
					_logger.LogWarning("Unknown database type: {Database}, skipping this cycle", currentDatabase);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error determining current database type");
			}
		}

		private async Task ProcessPostgresOutbox(IServiceScope scope, CancellationToken stoppingToken)
		{
			try
			{
				// Проверяем и запускаем LISTEN/NOTIFY если еще не запущен
				await EnsurePostgresListenStarted(stoppingToken);

				// ВАЖНО: Выполняем polling ТОЛЬКО если LISTEN/NOTIFY не работает
				if (_postgresListenCts == null || _postgresListenCts.Token.IsCancellationRequested)
				{
					_logger.LogDebug("LISTEN/NOTIFY not active, running polling fallback");
					var postgresRepo = scope.ServiceProvider.GetRequiredService<IPostgresRepository<OutboxMessage>>();
					await ProcessOutboxMessages(postgresRepo, stoppingToken);
				}
				else
				{
					_logger.LogDebug("LISTEN/NOTIFY active, skipping polling to avoid duplicates");

					// Выполняем только cleanup без обработки сообщений
					var postgresRepo = scope.ServiceProvider.GetRequiredService<IPostgresRepository<OutboxMessage>>();
					await postgresRepo.DeleteOldMessagesAsync(TimeSpan.FromSeconds(5));
					_logger.LogDebug("Completed cleanup of old PostgreSQL messages");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing PostgreSQL outbox");

				// При ошибке останавливаем LISTEN/NOTIFY
				await StopPostgresListen();
			}
		}

		private async Task ProcessMongoOutbox(IServiceScope scope, CancellationToken stoppingToken)
		{
			try
			{
				var mongoRepo = scope.ServiceProvider.GetRequiredService<IMongoRepository<OutboxMessage>>();

				// Останавливаем PostgreSQL LISTEN/NOTIFY если был запущен
				await StopPostgresListen();

				// Для MongoDB ВСЕГДА выполняем polling, так как Change Streams не работают в standalone
				_logger.LogDebug("Processing MongoDB outbox via polling");
				await ProcessOutboxMessages(mongoRepo, stoppingToken);

				// Дополнительно пытаемся запустить Change Streams (если вдруг появится replica set)
				await EnsureMongoChangeStreamStarted(stoppingToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing MongoDB outbox");

				// При ошибке останавливаем Change Streams
				await StopMongoChangeStream();
			}
		}

		private async Task EnsurePostgresListenStarted(CancellationToken stoppingToken)
		{
			// Останавливаем MongoDB Change Streams если были запущены
			await StopMongoChangeStream();

			// Если PostgreSQL LISTEN уже запущен, ничего не делаем
			if (_postgresListenCts != null && !_postgresListenCts.Token.IsCancellationRequested)
			{
				return;
			}

			try
			{
				_postgresListenCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

				_logger.LogInformation("Starting PostgreSQL LISTEN/NOTIFY monitoring...");

				// Запускаем LISTEN/NOTIFY в фоне
				_ = Task.Run(async () =>
				{
					try
					{
						await ProcessPostgresListenAsync(_postgresListenCts.Token);
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error in PostgreSQL LISTEN/NOTIFY background task");
					}
				}, _postgresListenCts.Token);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to start PostgreSQL LISTEN/NOTIFY");
			}
		}

		private async Task EnsureMongoChangeStreamStarted(CancellationToken stoppingToken)
		{
			// Останавливаем PostgreSQL LISTEN если был запущен
			await StopPostgresListen();

			// Если MongoDB Change Stream уже запущен, ничего не делаем
			if (_changeStreamCts != null && !_changeStreamCts.Token.IsCancellationRequested)
			{
				return;
			}

			try
			{
				_changeStreamCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

				_logger.LogInformation("Attempting to start MongoDB Change Stream monitoring...");

				// Запускаем Change Stream в фоне (может не сработать в standalone)
				_ = Task.Run(async () =>
				{
					try
					{
						await ProcessMongoChangeStreamAsync(_changeStreamCts.Token);
					}
					catch (MongoDB.Driver.MongoCommandException ex) when (ex.ErrorMessage.Contains("replica sets"))
					{
						_logger.LogWarning("MongoDB Change Streams not available (standalone mode), relying on polling");
					}
					catch (Exception ex)
					{
						_logger.LogWarning(ex, "MongoDB Change Stream failed, relying on polling");
					}
				}, _changeStreamCts.Token);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Failed to start MongoDB Change Stream, relying on polling");
			}
		}

		private async Task StopPostgresListen()
		{
			if (_postgresListenCts != null)
			{
				_logger.LogInformation("Stopping PostgreSQL LISTEN/NOTIFY...");
				_postgresListenCts.Cancel();
				_postgresListenCts.Dispose();
				_postgresListenCts = null;

				// Даем время на корректное завершение
				await Task.Delay(500);
			}
		}

		private async Task StopMongoChangeStream()
		{
			if (_changeStreamCts != null)
			{
				_logger.LogInformation("Stopping MongoDB Change Stream...");
				_changeStreamCts.Cancel();
				_changeStreamCts.Dispose();
				_changeStreamCts = null;

				// Даем время на корректное завершение
				await Task.Delay(500);
			}
		}

		private async Task ProcessPostgresListenAsync(CancellationToken stoppingToken)
		{
			try
			{
				using var scope = _serviceScopeFactory.CreateScope();
				var postgresRepo = scope.ServiceProvider.GetRequiredService<IPostgresRepository<OutboxMessage>>();

				var connectionString = GetPostgresConnectionString(postgresRepo);

				if (string.IsNullOrEmpty(connectionString))
				{
					_logger.LogWarning("Cannot get PostgreSQL connection string for LISTEN/NOTIFY");
					return;
				}

				using var connection = new NpgsqlConnection(connectionString);
				await connection.OpenAsync(stoppingToken);

				using var command = new NpgsqlCommand("LISTEN outbox_new_message", connection);
				await command.ExecuteNonQueryAsync(stoppingToken);

				_logger.LogInformation("PostgreSQL LISTEN/NOTIFY started successfully");

				connection.Notification += async (sender, e) =>
				{
					try
					{
						if (e.Channel == "outbox_new_message" && !string.IsNullOrEmpty(e.Payload))
						{
							_logger.LogDebug("Received NOTIFY for message ID: {MessageId}", e.Payload);
							_ = ProcessPostgresSingleMessageAsync(e.Payload, stoppingToken);
						}
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error processing PostgreSQL notification");
					}
				};

				while (!stoppingToken.IsCancellationRequested)
				{
					await connection.WaitAsync(stoppingToken);
				}
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("PostgreSQL LISTEN/NOTIFY stopped by cancellation");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in PostgreSQL LISTEN/NOTIFY processing");
			}
		}

		private async Task ProcessMongoChangeStreamAsync(CancellationToken cancellationToken)
		{
			try
			{
				using var scope = _serviceScopeFactory.CreateScope();
				var mongoRepo = scope.ServiceProvider.GetRequiredService<IMongoRepository<OutboxMessage>>();

				if (mongoRepo.GetType().GetMethod("GetCollection") == null)
				{
					_logger.LogWarning("MongoDB repository doesn't support Change Streams");
					return;
				}

				var collection = mongoRepo.GetCollection();

				var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<OutboxMessage>>()
					.Match(change => change.OperationType == ChangeStreamOperationType.Insert);

				var options = new ChangeStreamOptions
				{
					FullDocument = ChangeStreamFullDocumentOption.UpdateLookup
				};

				using var changeStream = await collection.WatchAsync(pipeline, options, cancellationToken);
				_logger.LogInformation("MongoDB Change Stream started successfully");

				while (await changeStream.MoveNextAsync(cancellationToken))
				{
					foreach (var change in changeStream.Current)
					{
						if (cancellationToken.IsCancellationRequested)
							break;

						try
						{
							var newMessage = change.FullDocument;
							if (newMessage != null && !newMessage.IsProcessed && !newMessage.IsDeadLetter)
							{
								_ = ProcessMongoSingleMessageAsync(newMessage, cancellationToken);
							}
						}
						catch (Exception ex)
						{
							_logger.LogError(ex, "Error processing change stream document");
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("MongoDB Change Stream stopped by cancellation");
			}
			catch (MongoDB.Driver.MongoCommandException ex) when (ex.ErrorMessage.Contains("replica sets"))
			{
				_logger.LogWarning("MongoDB Change Streams require replica sets. Running in standalone mode - using polling only");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in MongoDB Change Stream processing");
			}
		}

		private async Task ProcessPostgresSingleMessageAsync(string messageId, CancellationToken cancellationToken)
		{
			var startTime = DateTime.UtcNow;
			await _semaphore.WaitAsync(cancellationToken);
			var semaphoreWaitTime = DateTime.UtcNow - startTime;

			try
			{
				using var scope = _serviceScopeFactory.CreateScope();

				// ВАЖНО: Проверяем тип БД перед обработкой
				var factory = scope.ServiceProvider.GetRequiredService<IMessageProcessingServiceFactory>();
				var currentDatabase = factory.GetCurrentDatabaseType();

				if (currentDatabase != "postgres")
				{
					_logger.LogDebug("Database switched from PostgreSQL to {NewDatabase}, ignoring NOTIFY", currentDatabase);
					return;
				}

				var postgresRepo = scope.ServiceProvider.GetRequiredService<IPostgresRepository<OutboxMessage>>();

				if (!Guid.TryParse(messageId, out var messageGuid))
				{
					_logger.LogWarning("Invalid message ID format: {MessageId}", messageId);
					return;
				}

				var checkStartTime = DateTime.UtcNow;
				var message = await postgresRepo.GetByIdAsync(messageGuid);
				var checkTime = DateTime.UtcNow - checkStartTime;

				if (message == null || message.IsProcessed)
				{
					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogDebug("Message {MessageId} already processed or not found - Check: {CheckTime}ms, Total: {TotalTime}ms",
						messageId, checkTime.TotalMilliseconds, totalTime.TotalMilliseconds);
					return;
				}

				try
				{
					var publishStartTime = DateTime.UtcNow;
					await _rabbitMqService.PublishMessageAsync(
						message.InQueue,
						message.RoutingKey ?? message.InQueue,
						message.Payload);
					var publishTime = DateTime.UtcNow - publishStartTime;

					var updateStartTime = DateTime.UtcNow;
					message.MarkAsProcessed();
					await postgresRepo.UpdateMessagesAsync(new[] { message });
					var updateTime = DateTime.UtcNow - updateStartTime;

					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogInformation("Message {MessageId} published via PostgreSQL LISTEN/NOTIFY - Semaphore: {SemaphoreWait}ms, Check: {CheckTime}ms, Publish: {PublishTime}ms, Update: {UpdateTime}ms, Total: {TotalTime}ms",
						messageId, semaphoreWaitTime.TotalMilliseconds, checkTime.TotalMilliseconds, publishTime.TotalMilliseconds, updateTime.TotalMilliseconds, totalTime.TotalMilliseconds);
				}
				catch (Exception ex)
				{
					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogError(ex, "Failed to publish message {MessageId} via LISTEN/NOTIFY after {TotalTime}ms", messageId, totalTime.TotalMilliseconds);

					message.MarkAsRetried(ex.Message);
					await postgresRepo.UpdateMessagesAsync(new[] { message });
				}
			}
			finally
			{
				_semaphore.Release();
			}
		}

		private async Task ProcessMongoSingleMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
		{
			var startTime = DateTime.UtcNow;
			await _semaphore.WaitAsync(cancellationToken);
			var semaphoreWaitTime = DateTime.UtcNow - startTime;

			try
			{
				using var scope = _serviceScopeFactory.CreateScope();

				// ВАЖНО: Проверяем тип БД перед обработкой
				var factory = scope.ServiceProvider.GetRequiredService<IMessageProcessingServiceFactory>();
				var currentDatabase = factory.GetCurrentDatabaseType();

				if (currentDatabase != "mongo")
				{
					_logger.LogDebug("Database switched from MongoDB to {NewDatabase}, ignoring Change Stream event", currentDatabase);
					return;
				}

				var mongoRepo = scope.ServiceProvider.GetRequiredService<IMongoRepository<OutboxMessage>>();

				var checkStartTime = DateTime.UtcNow;
				var currentMessage = await mongoRepo.GetByIdAsync(message.Id.ToString());
				var checkTime = DateTime.UtcNow - checkStartTime;

				if (currentMessage == null || currentMessage.IsProcessed)
				{
					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogDebug("Message {MessageId} already processed or not found - Check: {CheckTime}ms, Total: {TotalTime}ms",
						message.Id, checkTime.TotalMilliseconds, totalTime.TotalMilliseconds);
					return;
				}

				try
				{
					var publishStartTime = DateTime.UtcNow;
					await _rabbitMqService.PublishMessageAsync(
						message.InQueue,
						message.RoutingKey ?? message.InQueue,
						message.Payload);
					var publishTime = DateTime.UtcNow - publishStartTime;

					var updateStartTime = DateTime.UtcNow;
					message.MarkAsProcessed();
					await mongoRepo.UpdateAsync(message.Id.ToString(), message);
					var updateTime = DateTime.UtcNow - updateStartTime;

					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogInformation("Message {MessageId} published via MongoDB Change Stream - Semaphore: {SemaphoreWait}ms, Check: {CheckTime}ms, Publish: {PublishTime}ms, Update: {UpdateTime}ms, Total: {TotalTime}ms",
						message.Id, semaphoreWaitTime.TotalMilliseconds, checkTime.TotalMilliseconds, publishTime.TotalMilliseconds, updateTime.TotalMilliseconds, totalTime.TotalMilliseconds);
				}
				catch (Exception ex)
				{
					var totalTime = DateTime.UtcNow - startTime;
					_logger.LogError(ex, "Failed to publish message {MessageId} via Change Stream after {TotalTime}ms", message.Id, totalTime.TotalMilliseconds);

					message.MarkAsRetried(ex.Message);
					await mongoRepo.UpdateAsync(message.Id.ToString(), message);
				}
			}
			finally
			{
				_semaphore.Release();
			}
		}

		private async Task ProcessOutboxMessages<TRepo>(TRepo repository, CancellationToken cancellationToken)
				where TRepo : IBaseRepository<OutboxMessage>
		{
			try
			{
				var messages = await repository.GetUnprocessedMessagesAsync();

				if (!messages.Any())
				{
					_logger.LogDebug("No unprocessed messages found");
					return;
				}

				var processedMessages = new List<OutboxMessage>();
				var failedMessages = new List<OutboxMessage>();

				var tasks = messages.Select(async message =>
				{
					if (cancellationToken.IsCancellationRequested)
						return;

					await _semaphore.WaitAsync(cancellationToken);
					try
					{
						try
						{
							await _rabbitMqService.PublishMessageAsync(
								message.InQueue,
								message.RoutingKey ?? message.InQueue,
								message.Payload);

							message.MarkAsProcessed();

							lock (processedMessages)
							{
								processedMessages.Add(message);
							}

							_logger.LogInformation("Message {MessageId} published to queue {Queue}",
								message.Id, message.InQueue);
						}
						catch (Exception ex)
						{
							_logger.LogError(ex, "Failed to publish message {MessageId}", message.Id);

							message.MarkAsRetried(ex.Message);

							lock (failedMessages)
							{
								failedMessages.Add(message);
							}
						}
					}
					finally
					{
						_semaphore.Release();
					}
				});

				await Task.WhenAll(tasks);

				if (processedMessages.Any())
				{
					await repository.UpdateMessagesAsync(processedMessages);
					_logger.LogInformation("Successfully processed {Count} messages", processedMessages.Count);
				}

				if (failedMessages.Any())
				{
					await repository.UpdateMessagesAsync(failedMessages);
					_logger.LogWarning("Failed to process {Count} messages", failedMessages.Count);
				}

				await repository.DeleteOldMessagesAsync(TimeSpan.FromSeconds(5));
				_logger.LogDebug("Completed cleanup of old messages");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error processing outbox messages");
			}
		}

		private string GetPostgresConnectionString<TRepo>(TRepo repository) where TRepo : IBaseRepository<OutboxMessage>
		{
			try
			{
				using var scope = _serviceScopeFactory.CreateScope();
				var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

				var postgresSettings = configuration.GetSection("PostgresDbSettings").Get<PostgresDbSettings>();
				if (postgresSettings != null)
				{
					var connectionString = postgresSettings.GetConnectionString();
					_logger.LogDebug("Found PostgreSQL connection string from PostgresDbSettings");
					return connectionString;
				}

				var connectionString2 = configuration.GetConnectionString("DefaultConnection")
									 ?? configuration.GetConnectionString("PostgreSQL")
									 ?? configuration.GetConnectionString("Database");

				if (!string.IsNullOrEmpty(connectionString2))
				{
					_logger.LogDebug("Found PostgreSQL connection string from ConnectionStrings");
					return connectionString2;
				}

				_logger.LogWarning("Could not find PostgreSQL connection string in configuration");
				return "";
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Error retrieving PostgreSQL connection string");
				return "";
			}
		}

		public override void Dispose()
		{
			_changeStreamCts?.Cancel();
			_changeStreamCts?.Dispose();
			_postgresListenCts?.Cancel();
			_postgresListenCts?.Dispose();
			_semaphore?.Dispose();
			base.Dispose();
		}
	}
}

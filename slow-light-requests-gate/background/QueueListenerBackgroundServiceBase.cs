using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.repositories;
using listenersrabbit;
using lazy_light_requests_gate.listenersrabbit;

namespace lazy_light_requests_gate.background
{
	public abstract class QueueListenerBackgroundServiceBase<TRepository> : BackgroundService
		where TRepository : class, IBaseRepository<QueuesEntity>
	{
		protected readonly IServiceScopeFactory _scopeFactory;
		protected readonly ILogger _logger;

		protected QueueListenerBackgroundServiceBase(
			IServiceScopeFactory scopeFactory,
			ILogger logger)
		{
			_scopeFactory = scopeFactory;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.LogInformation("{ServiceName}: пробуем запуск фонового сервиса прослушивания очередей.", GetType().Name);

			try
			{
				using var scope = _scopeFactory.CreateScope();
				var queueListener = scope.ServiceProvider.GetRequiredService<IRabbitMqQueueListener<RabbitMqQueueListener>>();
				var queuesRepository = scope.ServiceProvider.GetRequiredService<TRepository>();

				var elements = await queuesRepository.GetAllAsync();

				if (elements == null || !elements.Any())
				{
					_logger.LogInformation("Нет конкретных очередей для прослушивания. Слушатели rabbit не будут запущены.");
					return;
				}

				var validQueues = GetValidQueues(elements);

				if (!validQueues.Any())
				{
					_logger.LogWarning("Все записи в очередях имеют пустой OutQueueName. Слушатели не будут запущены.");
					return;
				}

				var listeningTasks = validQueues
					.Select(element =>
					{
						_logger.LogInformation("Инициализация слушателя очереди: {Queue}", element.OutQueueName);
						return Task.Run(() => queueListener.StartListeningAsync(element.OutQueueName, stoppingToken), stoppingToken);
					})
					.ToList();

				await Task.WhenAll(listeningTasks);

				foreach (var item in validQueues)
				{
					_logger.LogInformation("Слушатель для очереди {Queue} запущен.", item.OutQueueName);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка при запуске слушателей очередей.");
			}
		}

		/// <summary>
		/// Фильтрация валидных очередей. Может быть переопределена в наследниках для специфичной логики.
		/// </summary>
		protected virtual IEnumerable<QueuesEntity> GetValidQueues(IEnumerable<QueuesEntity> elements)
		{
			return elements.Where(e => !string.IsNullOrWhiteSpace(e.OutQueueName));
		}
	}
}

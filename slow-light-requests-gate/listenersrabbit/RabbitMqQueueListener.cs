using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using lazy_light_requests_gate.listenersrabbit;

namespace listenersrabbit
{
	public class RabbitMqQueueListener : IRabbitMqQueueListener<RabbitMqQueueListener>
	{
		private readonly ILogger<RabbitMqQueueListener> _logger;
		private readonly IConnection _connection;
		private IModel _channel;

		public RabbitMqQueueListener(IRabbitMqService rabbitMqService, ILogger<RabbitMqQueueListener> logger)
		{
			_connection = rabbitMqService.CreateConnection();
			_logger = logger;
		}

		public async Task StartListeningAsync(
			string queueOutName,
			CancellationToken stoppingToken,
			string pathForSave = null,
			Func<string, Task> onMessageReceived = null)
		{
			_channel = _connection.CreateModel();

			if (string.IsNullOrWhiteSpace(queueOutName))
			{
				_logger.LogError("Имя очереди не задано. Остановка слушателя.");
				throw new ArgumentException("Имя очереди не может быть пустым", nameof(queueOutName));
			}

			if (!QueueExists(queueOutName))
			{
				_logger.LogWarning("Очередь {Queue} не найдена. Создаю новую.", queueOutName);
				CreateQueue(queueOutName);
			}
			else
			{
				_logger.LogInformation("Очередь {Queue} найдена. Подключаюсь.", queueOutName);
			}

			var consumer = new EventingBasicConsumer(_channel);

			consumer.Received += async (model, ea) =>
			{
				var message = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray());

				if (onMessageReceived != null)
				{
					await onMessageReceived(message);
				}
				else
				{
					await ProcessMessageAsync(message, queueOutName);
				}
			};

			_logger.LogInformation("Подключен к очереди {Queue}. Ожидание сообщений...", queueOutName);
			_channel.BasicConsume(queue: queueOutName, autoAck: true, consumer: consumer);

			try
			{
				await Task.Delay(Timeout.Infinite, stoppingToken);
			}
			catch (TaskCanceledException)
			{
				_logger.LogInformation("Остановка слушателя очереди {Queue}.", queueOutName);
			}
		}

		/// <summary>
		/// Делаем что-то с полученным сообщением. Пока что логируем.
		/// </summary>
		/// <param name="message"></param>
		/// <param name="queueName"></param>
		/// <returns></returns>
		protected virtual Task ProcessMessageAsync(string message, string queueName)
		{
			_logger.LogInformation("Сообщение получено и обработано сообщения из {Queue}: {Message}", queueName, message);
			return Task.CompletedTask;
		}

		private void CreateQueue(string queueName)
		{
			if (_channel == null || !_channel.IsOpen)
			{
				_logger.LogWarning("Канал RabbitMQ закрыт. Пересоздаю канал перед созданием очереди...");
				_channel = _connection.CreateModel();
			}

			_logger.LogInformation("Создание очереди {Queue}", queueName);

			_channel.QueueDeclare(
				queue: queueName,
				durable: true,
				exclusive: false,
				autoDelete: false,
				arguments: null);
		}

		public void StopListening()
		{
			_logger.LogInformation("Остановка RabbitMQ слушателя...");
			_channel?.Close();
		}

		private bool QueueExists(string queueName)
		{
			if (string.IsNullOrWhiteSpace(queueName))
			{
				_logger.LogError("Проверка очереди: имя очереди пустое.");
				throw new ArgumentException("Имя очереди не может быть пустым", nameof(queueName));
			}

			try
			{
				_channel.QueueDeclarePassive(queueName);
				return true;
			}
			catch (RabbitMQ.Client.Exceptions.OperationInterruptedException ex)
			{
				_logger.LogWarning("Очередь {Queue} не существует: {Message}", queueName, ex.Message);
				return false;
			}
		}
	}
}

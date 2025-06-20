using lazy_light_requests_gate.common;
using lazy_light_requests_gate.entities;

namespace lazy_light_requests_gate.processing
{
	public abstract class MessageProcessingServiceBase : IMessageProcessingService
	{
		protected readonly ILogger _logger;

		protected MessageProcessingServiceBase(ILogger logger)
		{
			_logger = logger;
		}

		public async Task ProcessIncomingMessageAsync(
			string message,
			string instanceModelQueueOutName,
			string instanceModelQueueInName,
			string host,
			int? port,
			string protocol)
		{
			try
			{
				var outboxMessage = MessageFactory.CreateOutboxMessage(message, instanceModelQueueInName, instanceModelQueueOutName, protocol, host, port);

				var incidentEntity = MessageFactory.CreateIncidentEntity(message, protocol);

				await SaveOutboxMessageAsync(outboxMessage);
				await SaveIncidentAsync(incidentEntity);
			}
			catch (Exception ex)
			{
				_logger.LogError("Ошибка при обработке сообщения.");
				_logger.LogError(ex.Message);
			}
		}

		protected abstract Task SaveOutboxMessageAsync(OutboxMessage message);
		protected abstract Task SaveIncidentAsync(IncidentEntity incident);
	}
}

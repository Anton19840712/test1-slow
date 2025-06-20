using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.repositories;

namespace lazy_light_requests_gate.processing
{
	// Сервис обработки сообщений:
	public class MessageProcessingMongoService : MessageProcessingServiceBase
	{
		private readonly IMongoRepository<OutboxMessage> _outboxRepository;
		private readonly IMongoRepository<IncidentEntity> _incidentRepository;

		public MessageProcessingMongoService(
			IMongoRepository<OutboxMessage> outboxRepository,
			IMongoRepository<IncidentEntity> incidentRepository,
			ILogger<MessageProcessingMongoService> logger)
			: base(logger)
		{
			_outboxRepository = outboxRepository ?? throw new ArgumentNullException(nameof(outboxRepository));
			_incidentRepository = incidentRepository ?? throw new ArgumentNullException(nameof(incidentRepository));
		}

		protected override async Task SaveOutboxMessageAsync(OutboxMessage message)
		{
			await _outboxRepository.SaveMessageAsync(message);
		}

		protected override async Task SaveIncidentAsync(IncidentEntity incident)
		{
			await _incidentRepository.SaveMessageAsync(incident);
		}
	}
}


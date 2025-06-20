using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.repositories;

namespace lazy_light_requests_gate.processing
{
	public class MessageProcessingService<TOutboxRepo, TIncidentRepo> : MessageProcessingServiceBase
		where TOutboxRepo : IBaseRepository<OutboxMessage>
		where TIncidentRepo : IBaseRepository<IncidentEntity>
	{
		private readonly TOutboxRepo _outboxRepository;
		private readonly TIncidentRepo _incidentRepository;

		public MessageProcessingService(
			TOutboxRepo outboxRepository,
			TIncidentRepo incidentRepository,
			ILogger<MessageProcessingService<TOutboxRepo, TIncidentRepo>> logger)
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

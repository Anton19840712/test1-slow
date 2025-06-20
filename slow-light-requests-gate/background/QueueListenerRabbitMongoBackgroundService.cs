using lazy_light_requests_gate.background;
using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.repositories;

/// <summary>
/// Сервис слушает очередь bpm
/// </summary>
public class QueueListenerRabbitMongoBackgroundService : QueueListenerBackgroundServiceBase<IPostgresRepository<QueuesEntity>>
{
	public QueueListenerRabbitMongoBackgroundService(
		IServiceScopeFactory scopeFactory,
		ILogger<QueueListenerRabbitMongoBackgroundService> logger)
		: base(scopeFactory, logger)
	{
	}

	/// <summary>
	/// PostgreSQL версия имеет дополнительную валидацию очередей
	/// </summary>
	protected override IEnumerable<QueuesEntity> GetValidQueues(IEnumerable<QueuesEntity> elements)
	{
		return elements.Where(e => !string.IsNullOrWhiteSpace(e.OutQueueName));
	}
}

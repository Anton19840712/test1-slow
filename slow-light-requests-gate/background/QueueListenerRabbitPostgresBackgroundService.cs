using lazy_light_requests_gate.entities;
using lazy_light_requests_gate.repositories;
using lazy_light_requests_gate.background;

/// <summary>
/// Сервис слушает очередь bpm для PostgreSQL
/// </summary>
public class QueueListenerRabbitPostgresBackgroundService : QueueListenerBackgroundServiceBase<IPostgresRepository<QueuesEntity>>
{
	public QueueListenerRabbitPostgresBackgroundService(
		IServiceScopeFactory scopeFactory,
		ILogger<QueueListenerRabbitPostgresBackgroundService> logger)
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

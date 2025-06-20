namespace lazy_light_requests_gate.processing;

public class DynamicMessageProcessingService : IMessageProcessingService
{
	private readonly ILogger<DynamicMessageProcessingService> _logger;
	private readonly IConfiguration _configuration;
	private readonly IServiceProvider _serviceProvider;

	public DynamicMessageProcessingService(
		ILogger<DynamicMessageProcessingService> logger,
		IConfiguration configuration,
		IServiceProvider serviceProvider)
	{
		_logger = logger;
		_configuration = configuration;
		_serviceProvider = serviceProvider;
	}

	public async Task ProcessIncomingMessageAsync(string message, string queueOut, string queueIn, string host, int? port, string protocol)
	{
		var currentDatabase = _configuration["Database"]?.ToString()?.ToLower() ?? "mongo";

		IMessageProcessingService service;

		if (currentDatabase == "postgres")
		{
			service = _serviceProvider.GetRequiredService<MessageProcessingPostgresService>();
			_logger.LogInformation("Using PostgreSQL message processing service");
		}
		else
		{
			service = _serviceProvider.GetRequiredService<MessageProcessingMongoService>();
			_logger.LogInformation("Using MongoDB message processing service");
		}

		await service.ProcessIncomingMessageAsync(message, queueOut, queueIn, host, port, protocol);
	}
}

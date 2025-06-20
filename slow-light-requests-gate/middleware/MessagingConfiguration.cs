using lazy_light_requests_gate.processing;

namespace lazy_light_requests_gate.middleware
{
	static class MessagingConfiguration
	{
		/// <summary>
		/// Регистрация сервисов, участвующих в отсылке и получении сообщений на основе параметра Database.
		/// </summary>
		public static IServiceCollection AddMessageServingServices(this IServiceCollection services, IConfiguration configuration)
		{
			var database = configuration["Database"]?.ToString()?.ToLower() ?? "mongo";

			// Регистрируем оба сервиса
			services.AddTransient<MessageProcessingPostgresService>();
			services.AddTransient<MessageProcessingMongoService>();

			// Регистрируем фабрику
			services.AddScoped<IMessageProcessingServiceFactory, MessageProcessingServiceFactory>();

			// Регистрируем основной сервис на основе конфигурации
			if (database == "postgres")
			{
				services.AddTransient<IMessageProcessingService, MessageProcessingPostgresService>();
			}
			else // mongo по умолчанию
			{
				services.AddTransient<IMessageProcessingService, MessageProcessingMongoService>();
			}

			return services;
		}
	}
}

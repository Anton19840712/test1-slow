using lazy_light_requests_gate.background;

namespace lazy_light_requests_gate.middleware
{
	static class HostedServicesConfiguration
	{
		/// <summary>
		/// Регистрация фоновых сервисов приложения на основе параметра Database.
		/// </summary>
		public static IServiceCollection AddHostedServices(this IServiceCollection services, IConfiguration configuration)
		{
			var database = configuration["Database"]?.ToString()?.ToLower() ?? "mongo";

			// Регистрируем обычные сервисы для возможности их получения через DI
			services.AddTransient<QueueListenerRabbitPostgresBackgroundService>();
			services.AddTransient<QueueListenerRabbitMongoBackgroundService>();

			// Регистрируем динамические фоновые сервисы
			services.AddHostedService<DynamicOutboxBackgroundService>();

			// Регистрируем фоновые сервисы в зависимости от базы данных
			if (database == "postgres")
			{
				services.AddHostedService<QueueListenerRabbitPostgresBackgroundService>();
			}
			else if (database == "mongo") // mongo по умолчанию
			{
				services.AddHostedService<QueueListenerRabbitMongoBackgroundService>();
			}

			return services;
		}
	}
}

using lazy_light_requests_gate.headers;

namespace lazy_light_requests_gate.middleware
{
	static class HeadersConfiguration
	{
		/// <summary>
		/// Регистрация сервисов заголовков.
		/// </summary>
		public static IServiceCollection AddHeadersServices(this IServiceCollection services)
		{
			services.AddTransient<SimpleHeadersValidator>();
			services.AddTransient<DetailedHeadersValidator>();
			services.AddTransient<IHeaderValidationService, HeaderValidationService>();

			return services;
		}
	}
}

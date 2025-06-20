using FluentValidation;
using lazy_light_requests_gate.common;
using lazy_light_requests_gate.models;

namespace lazy_light_requests_gate.middleware
{
	static class ValidationConfiguration
	{
		/// <summary>
		/// Регистрация сервисов валидации.
		/// </summary>
		public static IServiceCollection AddValidationServices(this IServiceCollection services)
		{
			services.AddScoped<IServerInstanceFluentValidator, ServerInstanceFluentValidator>();
			services.AddScoped<IValidator<ServerInstanceModel>, ServerInstanceModelValidator>();

			return services;
		}
	}
}

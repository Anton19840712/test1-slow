using lazy_light_requests_gate.repositories;

namespace lazy_light_requests_gate.middleware
{
	public static class PostgresDbRepositoriesConfiguration
	{
		public static IServiceCollection AddPostgresDbRepositoriesServices(this IServiceCollection services, IConfiguration configuration)
		{
			services.AddScoped(typeof(IPostgresRepository<>), typeof(PostgresRepository<>));
			return services;
		}
	}
}

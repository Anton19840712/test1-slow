using lazy_light_requests_gate.repositories;

namespace lazy_light_requests_gate.middleware
{
	public static class MongoDbRepositoriesConfiguration
	{
		public static IServiceCollection AddMongoDbRepositoriesServices(this IServiceCollection services, IConfiguration configuration)
		{
			services.AddScoped(typeof(IMongoRepository<>), typeof(MongoRepository<>));
			return services;
		}
	}
}
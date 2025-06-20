using MongoDB.Driver;
using MongoDB.Bson;
using System.Security.Authentication;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using lazy_light_requests_gate.repositories;

namespace lazy_light_requests_gate.middleware
{
	static class MongoDbConfiguration
	{
		public static IServiceCollection AddMongoDbServices(this IServiceCollection services, IConfiguration configuration)
		{
			var mongoSettings = configuration.GetSection("MongoDbSettings");
			services.Configure<MongoDbSettings>(mongoSettings);

			var user = mongoSettings.GetValue<string>("User");
			var password = mongoSettings.GetValue<string>("Password");
			var connectionString = mongoSettings.GetValue<string>("ConnectionString");
			var databaseName = mongoSettings.GetValue<string>("DatabaseName");

			var mongoUrl = new MongoUrlBuilder(connectionString)
			{
				Username = user,
				Password = password
			}.ToString();

			// 🔐 Настраиваем сериализацию Guid-ов
			BsonDefaults.GuidRepresentationMode = GuidRepresentationMode.V3; // современный режим (рекомендуется)

			try
			{
				BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
			}
			catch (BsonSerializationException)
			{
				// Сериализатор уже зарегистрирован, игнорируем
			}


			var settings = MongoClientSettings.FromUrl(new MongoUrl(mongoUrl));
			settings.SslSettings = new SslSettings { EnabledSslProtocols = SslProtocols.Tls12 };

			// 👇 Регистрируем MongoClient
			services.AddSingleton<IMongoClient>(new MongoClient(settings));

			// 👇 Регистрируем базу
			services.AddSingleton(sp =>
			{
				var client = sp.GetRequiredService<IMongoClient>();
				return client.GetDatabase(databaseName);
			});
			services.AddScoped(typeof(IMongoRepository<>), typeof(MongoRepository<>));
			return services;
		}
	}
}

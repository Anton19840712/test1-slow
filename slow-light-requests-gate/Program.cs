using System.Text;
using lazy_light_requests_gate.middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Console.Title = "slow & light dynamic gate";

var builder = WebApplication.CreateBuilder(args);

LoggingConfiguration.ConfigureLogging(builder);
// Конфигурация динамического шлюза, шин, баз данных:
var (httpUrl, httpsUrl) = await GateConfiguration.ConfigureDynamicGateAsync(args, builder);

ConfigureServices(builder);

var app = builder.Build();

try
{

	await PostgresDbConfiguration.EnsureDatabaseInitializedAsync(app.Configuration);

	// Применяем настройки приложения
	ConfigureApp(app, httpUrl, httpsUrl);

	// Запускаем
	Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

	await app.RunAsync();
}
catch (Exception ex)
{
	Log.Fatal(ex, "Критическая ошибка при запуске приложения");
	throw;
}
finally
{
	Log.CloseAndFlush();
}

static void ConfigureServices(WebApplicationBuilder builder)
{
	var configuration = builder.Configuration;

	var services = builder.Services;

	services.AddControllers();

	services.AddCommonServices();
	services.AddHttpServices();
	services.AddRabbitMqServices(configuration);
	services.AddMessageServingServices(configuration);

	services.AddMongoDbServices(configuration);
	services.AddMongoDbRepositoriesServices(configuration);

	services.AddPostgresDbServices(configuration);
	services.AddPostgresDbRepositoriesServices(configuration);

	services.AddValidationServices();
	services.AddHostedServices(configuration);
	services.AddHeadersServices();

	// Авторизация и аутентификация: на данный момент эта часть не используется. Написана для будущего.
	services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
	{
		options.TokenValidationParameters = new TokenValidationParameters
		{
			ValidateIssuer = true,
			ValidIssuer = configuration["Jwt:Issuer"],
			ValidateAudience = true,
			ValidAudience = configuration["Jwt:Audience"],
			ValidateIssuerSigningKey = true,
			IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Secret"] ?? "supersecretkey")),
			ValidateLifetime = true
		};
	});

	services.AddAuthorization();
}

static void ConfigureApp(WebApplication app, string httpUrl, string httpsUrl)
{
	app.Urls.Add(httpUrl);
	app.Urls.Add(httpsUrl);
	Log.Information($"Middleware: динамический шлюз запущен и принимает запросы на следующих точках: {httpUrl} и {httpsUrl}");

	app.UseSerilogRequestLogging();

	app.UseCors(cors => cors
		.AllowAnyOrigin()
		.AllowAnyMethod()
		.AllowAnyHeader());

	app.UseAuthentication();
	app.UseAuthorization();

	app.MapControllers();
}

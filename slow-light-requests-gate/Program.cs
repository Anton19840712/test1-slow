using System.Text;
using lazy_light_requests_gate.middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Console.Title = "slow & light dynamic gate";

var builder = WebApplication.CreateBuilder(args);

LoggingConfiguration.ConfigureLogging(builder);
// ������������ ������������� �����, ���, ��� ������:
var (httpUrl, httpsUrl) = await GateConfiguration.ConfigureDynamicGateAsync(args, builder);

ConfigureServices(builder);

var app = builder.Build();

try
{

	await PostgresDbConfiguration.EnsureDatabaseInitializedAsync(app.Configuration);

	// ��������� ��������� ����������
	ConfigureApp(app, httpUrl, httpsUrl);

	// ���������
	Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

	await app.RunAsync();
}
catch (Exception ex)
{
	Log.Fatal(ex, "����������� ������ ��� ������� ����������");
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

	// ����������� � ��������������: �� ������ ������ ��� ����� �� ������������. �������� ��� ��������.
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
	Log.Information($"Middleware: ������������ ���� ������� � ��������� ������� �� ��������� ������: {httpUrl} � {httpsUrl}");

	app.UseSerilogRequestLogging();

	app.UseCors(cors => cors
		.AllowAnyOrigin()
		.AllowAnyMethod()
		.AllowAnyHeader());

	app.UseAuthentication();
	app.UseAuthorization();

	app.MapControllers();
}

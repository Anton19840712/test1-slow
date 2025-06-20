using Serilog;

namespace lazy_light_requests_gate.middleware
{
	static class LoggingConfiguration
	{
		/// <summary>
		/// Настройка Serilog для приложения
		/// </summary>
		public static void ConfigureLogging(WebApplicationBuilder builder)
		{
			builder.Host.UseSerilog((ctx, cfg) =>
			{
				cfg.MinimumLevel.Information()
				   .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
				   .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
				   .Enrich.FromLogContext();
			});
		}
	}
}
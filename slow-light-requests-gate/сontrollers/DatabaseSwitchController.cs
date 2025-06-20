using lazy_light_requests_gate.processing;
using Microsoft.AspNetCore.Mvc;

namespace lazy_light_requests_gate.сontrollers
{
	[ApiController]
	[Route("api/[controller]")]
	public class DatabaseSwitchController : ControllerBase
	{
		private readonly IMessageProcessingServiceFactory _messageProcessingServiceFactory;
		private readonly ILogger<DatabaseSwitchController> _logger;

		public DatabaseSwitchController(
			IMessageProcessingServiceFactory messageProcessingServiceFactory,
			ILogger<DatabaseSwitchController> logger)
		{
			_messageProcessingServiceFactory = messageProcessingServiceFactory;
			_logger = logger;
		}

		[HttpPost("switch")]
		public IActionResult SwitchDatabase([FromBody] DatabaseSwitchRequest request)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(request.DatabaseType))
				{
					return BadRequest("Database type is required");
				}

				var dbType = request.DatabaseType.ToLower();
				if (dbType != "mongo" && dbType != "postgres")
				{
					return BadRequest("Supported database types: 'mongo', 'postgres'");
				}

				_messageProcessingServiceFactory.SetDefaultDatabaseType(dbType);

				_logger.LogInformation("Database switched to: {DatabaseType}\n", dbType);

				return Ok(new
				{
					message = $"Database switched to {dbType}",
					currentDatabase = _messageProcessingServiceFactory.GetCurrentDatabaseType()
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error switching database");
				return StatusCode(500, "Internal server error");
			}
		}

		[HttpGet("current")]
		public IActionResult GetCurrentDatabase()
		{
			return Ok(new
			{
				currentDatabase = _messageProcessingServiceFactory.GetCurrentDatabaseType()
			});
		}
	}

	public class DatabaseSwitchRequest
	{
		public string DatabaseType { get; set; }
	}
}

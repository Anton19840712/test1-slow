using System.Text;
using lazy_light_requests_gate.headers;
using lazy_light_requests_gate.processing;
using Microsoft.AspNetCore.Mvc;

namespace lazy_light_requests_gate.Controllers;

[ApiController]
[Route("api/httpprotocol")]
public class HttpProtocolController : ControllerBase
{
	private readonly ILogger<HttpProtocolController> _logger;
	private readonly IHeaderValidationService _headerValidationService;
	private readonly IMessageProcessingServiceFactory _messageProcessingServiceFactory;
	private readonly IConfiguration _configuration;

	private string _message;

	public HttpProtocolController(
		ILogger<HttpProtocolController> logger,
		IHeaderValidationService headerValidationService,
		IMessageProcessingServiceFactory messageProcessingServiceFactory,
		IConfiguration configuration)
	{
		_logger = logger;
		_headerValidationService = headerValidationService;
		_messageProcessingServiceFactory = messageProcessingServiceFactory;
		_configuration = configuration;
	}

	//[Authorize] // в этой версии динамического шлюза мы отложили использование авторизации.
	[HttpPost("push")]
	public async Task<IActionResult> PushMessage()
	{
		var companyName = _configuration["CompanyName"] ?? "default-company";
		var host = _configuration["Host"] ?? "localhost";
		var port = _configuration["Port"] ?? "5000";
		var validate = bool.TryParse(_configuration["Validate"], out var v) && v;
		var protocol = Request.Scheme;

		// 🔍 Логируем заголовки
		_logger.LogInformation("Заголовки запроса:");
		foreach (var header in Request.Headers)
		{
			_logger.LogInformation("{Key}: {Value}", header.Key, header.Value.ToString());
		}
		_logger.LogInformation("Content-Type: {ContentType}", Request.ContentType);

		// 🔍 Логируем тело запроса (один раз можно прочитать Body)
		Request.EnableBuffering(); // <- важно! позволяет читать тело повторно

		using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
		{
			var body = await reader.ReadToEndAsync();
			_message = body;

			Request.Body.Position = 0;
			_logger.LogInformation("Тело запроса: {Body}", body);
		}
		_logger.LogInformation("Параметры шлюза: Company={Company}, Host={Host}, Port={Port}, Validate={Validate}, Protocol={Protocol}",
			companyName, host, port, validate, protocol);

		var queueOut = $"{companyName.Trim().ToLower()}_out";
		var queueIn = $"{companyName.Trim().ToLower()}_in";


		// если нужно валидировать из данных изначального файла конфигурации ("Validate": true там указано):
		if (validate)
		{
			//проверяем на наличие тега кастомной валидации:
			var isValid = await _headerValidationService.ValidateHeadersAsync(Request.Headers);
			if (!isValid)
			{
				_logger.LogWarning("Заголовки не прошли валидацию.");
				return BadRequest("Заголовки не прошли валидацию.");
			}
		}
		else
		{
			_logger.LogInformation("Валидация отключена.");
		}

		LogHeaders();

		// пока мы отправляем запрос, если flow позволяет это сделать и была пройдена валидация.
		var currentDatabaseType = _messageProcessingServiceFactory.GetCurrentDatabaseType();
		var messageProcessingService = _messageProcessingServiceFactory.CreateMessageProcessingService(currentDatabaseType);

		await messageProcessingService.ProcessIncomingMessageAsync(
			_message,
			queueOut,
			queueIn,
			host,
			int.TryParse(port, out var portInt) ? portInt : null,
			protocol
		);

		return Ok("Модель отправлена в шину и сохранена в БД.");
	}

	private void LogHeaders()
	{
		_logger.LogInformation("Получены заголовки запроса:");
		foreach (var header in Request.Headers)
		{
			_logger.LogInformation("  {Header}: {Value}", header.Key, header.Value);
		}
	}
}

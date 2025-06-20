using lazy_light_requests_gate.response;

namespace lazy_light_requests_gate.headers
{
	public class DetailedHeadersValidator : IHeadersValidator
	{
		private readonly ILogger<DetailedHeadersValidator> _logger;

		public DetailedHeadersValidator(ILogger<DetailedHeadersValidator> logger)
		{
			_logger = logger;
		}

		public Task<ResponseIntegration> ValidateHeadersAsync(IHeaderDictionary headers)
		{
			var errors = new List<string>();

			if (!headers.TryGetValue("Content-Type", out var contentType) || contentType != "application/json")
			{
				errors.Add("Invalid or missing Content-Type header. Expected 'application/json'");
			}

			// 3. Проверяем кастомный заголовок (если нужно)
			if (!headers.TryGetValue("X-Custom-Header", out var customHeader))
			{
				errors.Add("Missing X-Custom-Header");
			}

			// Если ошибки есть — логируем и возвращаем отрицательный ответ
			if (errors.Any())
			{
				foreach (var error in errors)
				{
					_logger.LogError(error);
				}

				return Task.FromResult(new ResponseIntegration
				{
					Message = string.Join("; ", errors),
					Result = false
				});
			}

			return Task.FromResult(new ResponseIntegration
			{
				Message = "Headers validation passed",
				Result = true
			});
		}
	}
}

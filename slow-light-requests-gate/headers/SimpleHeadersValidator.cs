using lazy_light_requests_gate.response;

namespace lazy_light_requests_gate.headers
{
	public class SimpleHeadersValidator : IHeadersValidator
	{
		public Task<ResponseIntegration> ValidateHeadersAsync(IHeaderDictionary headers)
		{
			// Минимальная проверка: просто наличие или отсутствие X-Custom-Header
			if (!headers.ContainsKey("X-Custom-Header"))
			{
				return Task.FromResult(new ResponseIntegration
				{
					Message = "Missing required header: X-Custom-Header",
					Result = false
				});
			}

			return Task.FromResult(new ResponseIntegration
			{
				Message = "Headers are valid.",
				Result = true
			});
		}
	}
}

using lazy_light_requests_gate.response;

namespace lazy_light_requests_gate.headers
{
	public interface IHeadersValidator
	{
		Task<ResponseIntegration> ValidateHeadersAsync(IHeaderDictionary headers);
	}
}

using lazy_light_requests_gate.models;
using lazy_light_requests_gate.response;

namespace lazy_light_requests_gate.common
{
	public interface IServerInstanceFluentValidator
	{
		ResponseIntegration Validate(ServerInstanceModel instanceModel);
	}
}

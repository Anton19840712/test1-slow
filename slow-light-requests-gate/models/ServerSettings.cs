using System.Text.Json.Serialization;

namespace lazy_light_requests_gate.models
{
	public record class ServerSettings : BaseConnectionSettings
	{
		[JsonPropertyName("clientHoldConnectionMs")]
		public int ClientHoldConnectionMs { get; set; }
	}
}

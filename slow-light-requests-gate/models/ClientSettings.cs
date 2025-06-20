using System.Text.Json.Serialization;

namespace lazy_light_requests_gate.models
{
	public record class ClientSettings : BaseConnectionSettings
	{
		[JsonPropertyName("attemptsToFindExternalServer")]
		public int AttemptsToFindExternalServer { get; set; }

		[JsonPropertyName("connectionTimeoutMs")]
		public int ConnectionTimeoutMs { get; set; }

		[JsonPropertyName("retryDelayMs")]
		public int RetryDelayMs { get; set; }
	}
}

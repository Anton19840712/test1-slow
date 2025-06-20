namespace lazy_light_requests_gate.common
{
	public static class Constants
	{
		// Константы для сервиса очистки
		public const int DefaultTtlDifferenceSeconds = 10;
		public const int DefaultCleanupIntervalSeconds = 10;
		public const int DefaultMessageProcessingDelayMs = 2000;
		public const string DefaultIpAddress = "default";
		public const string DefaultUserAgent = "server-instance";
		public const string RoutingKeyPrefix = "routing_key_";
	}
}
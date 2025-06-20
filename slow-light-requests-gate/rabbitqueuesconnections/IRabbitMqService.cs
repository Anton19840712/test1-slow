using RabbitMQ.Client;

public interface IRabbitMqService
{
	Task PublishMessageAsync(
		string queueName,
		string routingKey,
		string message);
	Task<string> WaitForResponseAsync(
		string queueName,
		int timeoutMilliseconds = 15000);
	IConnection CreateConnection();
}

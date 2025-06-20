using System.ComponentModel.DataAnnotations.Schema;
using lazy_light_requests_gate.models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization.Attributes;

namespace lazy_light_requests_gate
{
	public class OutboxMessage
	{
		[BsonId]
		//[BsonRepresentation(BsonType.String)]
		public Guid Id { get; set; }

		[BsonElement("modelType")]
		public ModelType ModelType { get; set; }

		[BsonElement("eventType")]
		public EventTypes EventType { get; set; }

		[BsonElement("isProcessed")]
		public bool IsProcessed { get; set; }

		[BsonElement("processedAt")]
		public DateTime ProcessedAt { get; set; }

		[BsonElement("outQueue")]
		public string OutQueue { get; set; }

		[BsonElement("inQueue")]
		public string InQueue { get; set; }

		[BsonElement("payload")]
		public string Payload { get; set; }

		[BsonElement("routing_key")]
		public string RoutingKey { get; set; }

		[BsonElement("createdAt")]
		[BsonRepresentation(BsonType.DateTime)]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		// Принудительно сохраняем в UTC
		[BsonElement("createdAtFormatted")]
		[BsonIgnoreIfNull]
		public string CreatedAtFormatted { get; set; }

		// Принудительно сохраняем в UTC
		[BsonElement("source")]
		[BsonIgnoreIfNull]
		public string Source { get; set; }

		// Новые поля для retry логики и отслеживания ошибок
		[BsonElement("retryCount")]
		[BsonIgnoreIfNull]
		public int? RetryCount { get; set; }

		[BsonElement("lastError")]
		[BsonIgnoreIfNull]
		public string? LastError { get; set; }

		[BsonElement("lastRetryAt")]
		[BsonIgnoreIfNull]
		[BsonRepresentation(BsonType.DateTime)]
		public DateTime? LastRetryAt { get; set; }

		// Максимальное количество попыток (можно сделать configurable)
		[BsonElement("maxRetries")]
		[BsonIgnoreIfDefault]
		public int MaxRetries { get; set; } = 3;

		// Для отслеживания когда сообщение стало "мертвым"
		[BsonElement("isDeadLetter")]
		[BsonIgnoreIfDefault]
		public bool IsDeadLetter { get; set; } = false;

		[BsonIgnore]
		[NotMapped]
		public string FormattedDate => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

		[BsonIgnore]
		[NotMapped]
		public bool ShouldRetry => !IsProcessed && !IsDeadLetter && (RetryCount ?? 0) < MaxRetries;

		[BsonIgnore]
		[NotMapped]
		public bool HasFailed => (RetryCount ?? 0) >= MaxRetries && !IsProcessed;

		public OutboxMessage()
		{
			CreatedAtFormatted = FormattedDate; // Заполняем перед сохранением
		}

		public string GetPayloadJson()
		{
			return Payload?.ToJson(new JsonWriterSettings()) ?? string.Empty;
		}

		// Методы для работы с retry логикой
		public void MarkAsRetried(string error)
		{
			RetryCount = (RetryCount ?? 0) + 1;
			LastError = error;
			LastRetryAt = DateTime.UtcNow;

			// Если превысили лимит попыток, помечаем как dead letter
			if (RetryCount >= MaxRetries)
			{
				IsDeadLetter = true;
			}
		}

		public void MarkAsProcessed()
		{
			IsProcessed = true;
			ProcessedAt = DateTime.UtcNow;
			LastError = null; // Очищаем ошибку при успешной обработке
		}
	}
}
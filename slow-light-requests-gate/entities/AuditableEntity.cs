using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace lazy_light_requests_gate.entities
{
	public abstract class AuditableEntity
	{
		[BsonId]
		//[BsonRepresentation(BsonType.ObjectId)]
		public Guid Id { get; set; }
		public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
		public DateTime? UpdatedAtUtc { get; set; }
		public DateTime? DeletedAtUtc { get; set; }

		public string CreatedBy { get; set; }
		public string UpdatedBy { get; set; }
		public string DeletedBy { get; set; }

		public bool IsDeleted { get; set; } = false;

		public int Version { get; set; } = 1;

		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
		public string CorrelationId { get; set; }
		public string ModelType { get; set; }
		public bool IsProcessed { get; set; }

		[BsonElement("createdAtFormatted")]
		[BsonIgnoreIfNull]
		public string CreatedAtFormatted { get; set; }

		[BsonIgnore]
		[NotMapped]
		public string FormattedDate => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

		public AuditableEntity()
		{
			CreatedAtFormatted = FormattedDate;
		}
	}
}

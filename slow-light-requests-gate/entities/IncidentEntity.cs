using MongoDB.Bson.Serialization.Attributes;

namespace lazy_light_requests_gate.entities
{
	[BsonIgnoreExtraElements]
	public class IncidentEntity : AuditableEntity
	{
		public string Payload { get; set; }
	}
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace lazy_light_requests_gate.models
{
	public record class ConnectionEndpoint
	{
		[JsonPropertyName("host")]
		public string Host { get; set; }

		[JsonPropertyName("port")]
		[JsonConverter(typeof(PortConverter))] // Применяем кастомный конвертер для порта
		public int? Port { get; set; }
	}

	public class PortConverter : JsonConverter<int?>
	{
		public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			// Если значение это строка, пробуем преобразовать его в число
			if (reader.TokenType == JsonTokenType.String)
			{
				if (int.TryParse(reader.GetString(), out int result))
				{
					return result;
				}
			}
			// Если это число, просто возвращаем его
			else if (reader.TokenType == JsonTokenType.Number)
			{
				return reader.GetInt32();
			}

			return null; // В случае ошибки вернем null
		}

		public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
		{
			// Записываем число или null
			writer.WriteNumberValue(value ?? 0);
		}
	}
}

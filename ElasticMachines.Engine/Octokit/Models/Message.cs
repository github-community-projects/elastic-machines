using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElasticMachines.Engine.Octokit.Models;

public record Message
{
    public required long MessageId { get; init; }
    [JsonConverter(typeof(BodyConverter))]
    public required List<JobMessage> Body { get; init; }
    public required Statistics Statistics { get; init; }

    public class BodyConverter : JsonConverter<List<JobMessage>>
    {
        public override List<JobMessage>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<List<JobMessage>>(reader.GetString() ?? throw new JsonException(), options);
        }

        public override void Write(Utf8JsonWriter writer, List<JobMessage> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, JsonSerializer.Serialize(value, options), options);
        }
    }
}



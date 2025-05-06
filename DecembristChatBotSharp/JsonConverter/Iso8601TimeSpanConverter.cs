using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace DecembristChatBotSharp.JsonConverter;

public class Iso8601TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(XmlConvert.ToString(value));
    }
        
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Expected string for TimeSpan");
        return XmlConvert.ToTimeSpan(text);
    }
}
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace DecembristChatBotSharp.JsonConverter;

public class BsonDocumentJsonConverter : JsonConverter<BsonDocument>
{
    public override BsonDocument Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return BsonDocument.Parse(doc.RootElement.GetRawText());
    }

    public override void Write(
        Utf8JsonWriter writer,
        BsonDocument value,
        JsonSerializerOptions options)
    {
        var json = value.ToJson();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.WriteTo(writer);
    }
}
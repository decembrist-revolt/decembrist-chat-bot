using System.Text.Json.Serialization;
using DecembristChatBotSharp.JsonConverter;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record UserProduct(
    [property: BsonId] string Id,
    string UserId,
    Product Product
);

public record Product(
    string Id,
    string Name,
    Price Price,
    string Description,
    int Count,
    bool Multiple,
    string ConfirmationMessage,
    [property: JsonConverter(typeof(BsonDocumentJsonConverter))]
    BsonDocument MetaInfo,
    AuditMetadata Audit
);

public record Price(decimal Amount, string Currency);

public record AuditMetadata(DateTime CreatedAt, DateTime UpdatedAt);

public enum ProductType
{
    ChatPremium = 0
}

public record ChatPremiumMetaInfo(
    long ChatId,
    [property: JsonConverter(typeof(Iso8601TimeSpanConverter))]
    TimeSpan Duration
);
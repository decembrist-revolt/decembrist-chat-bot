using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record ExpiredMessage(
    [property: BsonId]
    ExpiredMessage.CompositeId Id,
    DateTime ExpirationDate
)
{
    public record CompositeId(
        long ChatId,
        int MessageId
    );
}
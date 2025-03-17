using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record MemberLike(
    [property: BsonId] MemberLike.CompositeId Id,
    long LikeTelegramId,
    int Value
)
{
    public record CompositeId(
        long TelegramId,
        long ChatId
    );
}
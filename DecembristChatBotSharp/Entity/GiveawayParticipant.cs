using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record GiveawayParticipant(
    [property: BsonId] GiveawayParticipant.CompositeId Id,
    DateTime ReceivedAt,
    DateTime ExpireAt
)
{
    public record CompositeId(long ChatId, int MessageId, long TelegramId);
}

public enum GiveawayTargetAudience
{
    PremiumOnly,
    All
}


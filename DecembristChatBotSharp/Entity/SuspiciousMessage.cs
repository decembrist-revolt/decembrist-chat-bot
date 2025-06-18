using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record SuspiciousMessage(
    [property: BsonId] SuspiciousMessage.CompositeId Id,
    long OwnerId,
    int CaptchaMessageId,
    DateTime CreatedAt)
{
    public record CompositeId(long ChatId, int MessageId)
    {
        public static implicit operator CompositeId((long, int) tuple) => new(tuple.Item1, tuple.Item2);
    }
}
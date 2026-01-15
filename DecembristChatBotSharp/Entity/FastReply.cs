using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record FastReply(
    [property: BsonId] FastReply.CompositeId Id,
    long TelegramId,
    string Reply,
    DateTime ExpireAt,
    FastReplyType MessageType,
    FastReplyType ReplyType)
{
    public record CompositeId(long ChatId, string Message)
    {
        public static implicit operator CompositeId((long, string) tuple) => new(tuple.Item1, tuple.Item2);
    }
}

public enum FastReplyType
{
    Text,
    Sticker,
    Photo,
    Animation,
}
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record FilteredMessage(
    [property: BsonId] CompositeId Id,
    int MessageId,
    int CaptchaMessageId,
    DateTime CreatedAt,
    int TryCount = 0);
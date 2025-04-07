using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record DislikeMember(
    [property: BsonId] CompositeId Id,
    long DislikeTelegramId);
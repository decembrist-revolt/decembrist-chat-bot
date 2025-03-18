using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record AdminUser(
    [property: BsonId] long TelegramId
);
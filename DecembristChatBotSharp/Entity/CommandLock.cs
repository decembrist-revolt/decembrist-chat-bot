using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record CommandLock(
    [property: BsonId]
    CommandLock.CompositeId Id, 
    DateTime ExpiredTime)
{
    public record CompositeId(long ChatId, string Command, string? Arguments, long? TelegramId);
}
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public readonly record struct RestrictMember(
    [property: BsonId] RestrictMember.CompositeId Id,
    RestrictType RestrictType)
{
    public readonly record struct CompositeId(
        long TelegramId,
        long ChatId
    );
}

[Flags]
public enum RestrictType : short
{
    None = 0,
    Link = 1,
}
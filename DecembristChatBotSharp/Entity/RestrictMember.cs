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
    All = 0,
    Text = 1,
    Sticker = 2,
    Link = 4,
    Emoji = 8
}
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record RestrictMember(
    [property: BsonId] CompositeId Id,
    RestrictType RestrictType,
    int TimeoutMinutes = 0);

[Flags]
public enum RestrictType : short
{
    None = 0,
    Link = 1,
    Timeout = 2,
}
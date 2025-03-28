﻿using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record RestrictMember(
    [property: BsonId] CompositeId Id,
    RestrictType RestrictType);

[Flags]
public enum RestrictType : short
{
    None = 0,
    Link = 1,
}
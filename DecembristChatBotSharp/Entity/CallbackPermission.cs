﻿using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record CallbackPermission(
    [property: BsonId] CallbackPermission.CompositeId Id,
    DateTime ExpireAt
)
{
    public record CompositeId(long ChatId, long TelegramId, CallbackType type, int MessageId);
};

public enum CallbackType
{
    List,
    Giveaway
}
using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Entity;

public record CharmMember(
    [property: BsonId] CompositeId Id,
    string SecretWord,
    DateTime ExpireAt
);
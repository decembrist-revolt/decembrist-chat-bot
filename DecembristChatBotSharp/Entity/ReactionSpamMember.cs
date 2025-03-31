using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Entity;

public record ReactionSpamMember(
    [property: BsonId] CompositeId Id,
    ReactionTypeEmoji Emoji,
    DateTime ExpireAt
);
using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Entity;

public record ReactionMember(
    [property: BsonId] ReactionMember.CompositeId Id,
    ReactionTypeEmoji Emoji
)
{
    public record CompositeId(
        long TelegramId,
        long ChatId
    );
}
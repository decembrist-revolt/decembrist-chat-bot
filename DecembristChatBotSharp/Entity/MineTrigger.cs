using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Entity;

public record MineTrigger(
    [property: BsonId] MineTrigger.CompositeId Id,
    ReactionTypeEmoji Emoji,
    DateTime ExpireAt
)
{
    public record CompositeId(long TelegramId, long ChatId, string Trigger);
}


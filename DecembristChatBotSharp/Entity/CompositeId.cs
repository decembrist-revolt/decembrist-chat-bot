namespace DecembristChatBotSharp.Entity;

public record CompositeId(long TelegramId, long ChatId)
{
    public static implicit operator CompositeId((long TelegramId, long ChatId) id) => new(id.TelegramId, id.ChatId);
}
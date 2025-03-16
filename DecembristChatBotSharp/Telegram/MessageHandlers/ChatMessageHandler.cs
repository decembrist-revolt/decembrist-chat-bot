namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public readonly struct ChatMessageHandlerParams(
    IMessagePayload payload,
    int messageId,
    long telegramId,
    long chatId
)
{
    public IMessagePayload Payload => payload;
    public int MessageId => messageId;
    public long TelegramId => telegramId;
    public long ChatId => chatId;
}

public interface IMessagePayload;

public readonly struct TextPayload(string text) : IMessagePayload
{
    public string Text => text;
}

public readonly struct StickerPayload(string fileId) : IMessagePayload
{
    public string FileId => fileId;
}

public readonly struct UnknownPayload : IMessagePayload;

public class ChatMessageHandler(
    CaptchaHandler captchaHandler,
    FastReplyHandler fastReplyHandler
)
{
    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var result = await captchaHandler.Do(parameters);
        if (result == Result.Captcha) return unit;

        return await fastReplyHandler.Do(parameters);
    }
}
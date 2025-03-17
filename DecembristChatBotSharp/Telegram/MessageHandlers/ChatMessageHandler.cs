using MongoDB.Bson;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public readonly struct ChatMessageHandlerParams(
    IMessagePayload payload,
    int messageId,
    long telegramId,
    long chatId,
    Option<long> replyToTelegramId
)
{
    public IMessagePayload Payload => payload;
    public int MessageId => messageId;
    public long TelegramId => telegramId;
    public long ChatId => chatId;
    public Option<long> ReplyToTelegramId => replyToTelegramId;
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
    ChatCommandHandler chatCommandHandler,
    FastReplyHandler fastReplyHandler
)
{
    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var result = await captchaHandler.Do(parameters);
        if (result == Result.Captcha) return unit;
        
        if (parameters.Payload is TextPayload { Text.Length: > 1 })
        {
            var commandResult = await chatCommandHandler.Do(parameters);
            if (commandResult == CommandResult.Ok) return unit;
        }

        return await fastReplyHandler.Do(parameters);
    }
}
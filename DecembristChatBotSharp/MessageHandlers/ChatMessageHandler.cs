namespace DecembristChatBotSharp.MessageHandlers;

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

public class ChatMessageHandler(AppConfig appConfig, BotClient botClient, Database db)
{
    private readonly CaptchaHandler _captchaHandler = new(appConfig, botClient, db);
    private readonly FastReplyHandler _fastReplyHandler = new(appConfig, botClient);
    
    public async Task<Unit> Do(
        ChatMessageHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var result = await _captchaHandler.Do(parameters, cancelToken);
        if (result == Result.Captcha) return unit;
        
        return await _fastReplyHandler.Do(parameters, cancelToken);
    }
}

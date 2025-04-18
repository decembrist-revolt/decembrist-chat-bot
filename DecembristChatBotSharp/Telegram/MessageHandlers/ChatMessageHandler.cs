using JasperFx.Core;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public readonly record struct ChatMessageHandlerParams(
    IMessagePayload Payload,
    int MessageId,
    long TelegramId,
    long ChatId,
    Option<long> ReplyToTelegramId
)
{
    public void Deconstruct(out int messageId, out long telegramId, out long chatId)
    {
        messageId = MessageId;
        telegramId = TelegramId;
        chatId = ChatId;
    }
}

public interface IMessagePayload;

public readonly struct TextPayload(string text, bool isLink) : IMessagePayload
{
    public string Text => text;
    public bool IsLink => isLink;
}

public readonly struct StickerPayload(string fileId) : IMessagePayload
{
    public string FileId => fileId;
}

public readonly struct UnknownPayload : IMessagePayload;

[Singleton]
public class ChatMessageHandler(
    CaptchaHandler captchaHandler,
    ChatCommandHandler chatCommandHandler,
    FastReplyHandler fastReplyHandler,
    RestrictHandler restrictHandler,
    CharmHandler charmHandler,
    ReactionSpamHandler reactionSpamHandler,
    WrongCommandHandler wrongCommandHandler
)
{
    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var result = await captchaHandler.Do(parameters);
        if (result == Result.Captcha) return unit;

        await reactionSpamHandler.Do(parameters);
        if (await restrictHandler.Do(parameters)) return unit;
        if (await charmHandler.Do(parameters)) return unit;

        if (parameters.Payload is TextPayload { Text.Length: > 1, Text: var text })
        {
            var commandResult = await chatCommandHandler.Do(parameters);
            if (commandResult == CommandResult.Ok) return unit;

            if (await wrongCommandHandler.Do(parameters.ChatId, text, parameters.MessageId))
            {
                return unit;
            }
        }

        return await fastReplyHandler.Do(parameters);
    }
}
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public readonly record struct ChatMessageHandlerParams(
    IMessagePayload Payload,
    int MessageId,
    long TelegramId,
    long ChatId,
    Option<long> ReplyToTelegramId,
    bool BotMentioned,
    bool ReplyToBotMessage
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
    FilteredMessageHandler filteredMessageHandler,
    FilterCaptchaHandler filterCaptchaHandler,
    ChatCommandHandler chatCommandHandler,
    FastReplyHandler fastReplyHandler,
    RestrictHandler restrictHandler,
    MessageAssistance messageAssistance,
    CharmHandler charmHandler,
    CurseHandler curseHandler,
    MinaHandler minaHandler,
    WrongCommandHandler wrongCommandHandler,
    AiQueryHandler aiQueryHandler
)
{
    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var result = await captchaHandler.Do(parameters);
        if (result == Result.Captcha) return unit;

        await filterCaptchaHandler.Do(parameters);
        await filteredMessageHandler.Do(parameters);

        await curseHandler.Do(parameters);
        if (await minaHandler.Do(parameters)) return unit;
        if (await restrictHandler.Do(parameters)) return unit;
        if (await charmHandler.Do(parameters)) return unit;

        if (parameters.Payload is TextPayload { Text.Length: > 1, Text: var text })
        {
            var commandResult = await chatCommandHandler.Do(parameters);
            if (commandResult != CommandResult.None) return await HandleCommandResult(parameters, commandResult, text);

            if (await wrongCommandHandler.Do(parameters.ChatId, text, parameters.MessageId))
            {
                return unit;
            }
        }
        
        // Try fast reply first
        if (await fastReplyHandler.Do(parameters))
        {
            return unit;
        }

        aiQueryHandler.Do(parameters);

        return unit;
    }

    private async Task<Unit> HandleCommandResult(
        ChatMessageHandlerParams parameters, CommandResult commandResult, string text)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (commandResult == CommandResult.Ok) return unit;
        var taskResult = commandResult switch
        {
            CommandResult.NoAdmin => messageAssistance.SendAdminOnlyMessage(chatId, telegramId),
            CommandResult.NoItem => messageAssistance.SendNoItems(chatId),
            _ => throw new ArgumentOutOfRangeException(nameof(commandResult), commandResult, null)
        };
        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, text)).WhenAll();
    }
}
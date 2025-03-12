using Telegram.Bot;

namespace DecembristChatBotSharp.MessageHandlers;

public readonly struct PrivateMessageHandlerParams(
    long chatId,
    long telegramId,
    string? text
)
{
    public long ChatId => chatId;
    public long TelegramId => telegramId;
    public string? Text => text;
}

public class PrivateMessageHandler(BotClient botClient)
{
    public async Task<Unit> Do(PrivateMessageHandlerParams parameters, CancellationToken cancelToken) =>
        parameters.Text switch
        {
            _ => ignore(await botClient.SendMessage(parameters.ChatId, "OK", cancellationToken: cancelToken))
        };
}
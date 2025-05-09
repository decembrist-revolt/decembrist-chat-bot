using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreMessageAssistant(
    AppConfig appConfig,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<Message> SendNotFoundMessage(string key, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyNotFound, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendFailedMessage(long chatId)
    {
        var message = appConfig.LoreConfig.PrivateFailed;
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendExpiredMessage(string key, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.MessageExpired, key);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendHelpMessage(long chatId)
    {
        var loreConfig = appConfig.LoreConfig;
        var message = string.Format(loreConfig.LoreHelp, loreConfig.KeyLimit, loreConfig.ContentLimit);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }
}
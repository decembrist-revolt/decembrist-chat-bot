using DecembristChatBotSharp.Entity.Configs;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreMessageAssistant(
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    private const string LoreNotAvailable = "Лор не доступен в текущий момент попробуйте позже";
    
    public async Task<Message> SendNotFoundMessage(string key, long telegramId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.KeyNotFound, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendFailedMessage(long chatId, LoreConfig loreConfig)
    {
        var message = loreConfig.PrivateFailed;
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendExpiredMessage(string key, long chatId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.MessageExpired, key);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendHelpMessage(long chatId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.LoreHelp, loreConfig.KeyLimit, loreConfig.ContentLimit);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public async Task<Message> SendNotAvailableMessage(long chatId) => 
        await botClient.SendMessage(chatId, LoreNotAvailable, cancellationToken: cancelToken.Token);
}
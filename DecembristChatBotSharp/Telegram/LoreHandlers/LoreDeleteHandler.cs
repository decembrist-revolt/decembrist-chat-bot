using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreDeleteHandler(
    LoreService loreService,
    BotClient botClient,
    AppConfig appConfig,
    LoreMessageAssistant loreMessageAssistant,
    CancellationTokenSource cancelToken)
{
    public async Task<Message> Do(string messageText, long lorChatId, long telegramId, DateTime dateTime)
    {
        var key = messageText.ToLowerInvariant();
        var result = await loreService.DeleteLoreRecord(key, lorChatId, dateTime);
        return result switch
        {
            LoreResult.Success => await SendSuccessDelete(key, telegramId),
            LoreResult.NotFound => await loreMessageAssistant.SendNotFoundMessage(key, telegramId),
            LoreResult.Expire => await loreMessageAssistant.SendExpiredMessage(key, telegramId),
            _ => await loreMessageAssistant.SendFailedMessage(telegramId)
        };
    }

    private Task<Message> SendSuccessDelete(string key, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.DeleteSuccess, key);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }
}
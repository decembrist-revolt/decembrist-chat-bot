using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreDeleteHandler(
    LoreService loreService,
    BotClient botClient,
    LoreMessageAssistant loreMessageAssistant,
    CancellationTokenSource cancelToken)
{
    public async Task<Message> Do(string messageText, long lorChatId, long telegramId, DateTime dateTime, LoreConfig loreConfig)
    {
        var key = messageText.ToLowerInvariant();
        var result = await loreService.DeleteLoreRecord(key, lorChatId, dateTime);
        loreService.LogLore((uint) result, telegramId, lorChatId, key);
        return result switch
        {
            DeleteLoreRecordResult.Success => await SendSuccessDelete(key, telegramId, loreConfig),
            DeleteLoreRecordResult.NotFound => await loreMessageAssistant.SendNotFoundMessage(key, telegramId, loreConfig),
            DeleteLoreRecordResult.Expire => await loreMessageAssistant.SendExpiredMessage(key, telegramId, loreConfig),
            _ => await loreMessageAssistant.SendFailedMessage(telegramId, loreConfig)
        };
    }

    private Task<Message> SendSuccessDelete(string key, long telegramId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.DeleteSuccess, key);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }
}
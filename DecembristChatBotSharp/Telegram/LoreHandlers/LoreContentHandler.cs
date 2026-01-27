using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreContentHandler(
    LoreMessageAssistant loreMessageAssistant,
    LoreService loreService,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<Message> Do(
        string key, string content, long lorChatId, long telegramId, DateTime date, LoreConfig loreConfig)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.ChangeLoreContent(key, content, lorChatId, telegramId, date);
        loreService.LogLore((uint)result, telegramId, lorChatId, key, content);
        return result switch
        {
            ChangeLoreContentResult.Success => await SendSuccessContent(key, content, telegramId, loreConfig),
            ChangeLoreContentResult.NotFound => await loreMessageAssistant.SendNotFoundMessage(key, telegramId, loreConfig),
            ChangeLoreContentResult.Limit => await loreMessageAssistant.SendHelpMessage(telegramId, loreConfig),
            ChangeLoreContentResult.Failed => await loreMessageAssistant.SendFailedMessage(telegramId, loreConfig),
            ChangeLoreContentResult.Expire => await loreMessageAssistant.SendExpiredMessage(key, telegramId, loreConfig),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendSuccessContent(string key, string content, long telegramId, LoreConfig loreConfig)
    {
        var text = string.Format(loreConfig.ContentSuccess, key, content);
        return await botClient.SendMessage(telegramId, text, cancellationToken: cancelToken.Token);
    }
}
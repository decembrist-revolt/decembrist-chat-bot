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
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    public async Task<Message> Do(
        string key, string content, long lorChatId, long telegramId, DateTime date)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.ChangeLoreContent(key, content, lorChatId, telegramId, date);
        loreService.LogLore((uint)result, telegramId, lorChatId, key, content);
        return result switch
        {
            ChangeLoreContentResult.Success => await SendSuccessContent(key, content, telegramId),
            ChangeLoreContentResult.NotFound => await loreMessageAssistant.SendNotFoundMessage(key, telegramId),
            ChangeLoreContentResult.Limit => await loreMessageAssistant.SendHelpMessage(telegramId),
            ChangeLoreContentResult.Failed => await loreMessageAssistant.SendFailedMessage(telegramId),
            ChangeLoreContentResult.Expire => await loreMessageAssistant.SendExpiredMessage(key, telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendSuccessContent(string key, string content, long telegramId)
    {
        var text = string.Format(appConfig.LoreConfig.ContentSuccess, key, content);
        return await botClient.SendMessage(telegramId, text, cancellationToken: cancelToken.Token);
    }
}
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using static DecembristChatBotSharp.Telegram.LoreHandlers.LoreHandler;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreKeyHandler(
    LoreService loreService,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    LoreRecordRepository loreRecordRepository,
    LoreMessageAssistant loreMessageAssistant)
{
    public async Task<Message> Do(string key, long lorChatId, long telegramId)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.AddLoreKey(key, lorChatId, telegramId);
        result.LogLore(telegramId, lorChatId, key);
        return result switch
        {
            LoreResult.Success => await SendRequestContent(key, lorChatId, telegramId),
            LoreResult.Duplicate => await RetrieveAndSendLoreRecord((lorChatId, key), telegramId),
            LoreResult.Limit => await loreMessageAssistant.SendHelpMessage(telegramId),
            LoreResult.Failed => await loreMessageAssistant.SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendRequestContent(string key, long lorChatId, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.ContentRequest,
            LoreService.GetLoreTag(ContentSuffix, lorChatId, key));
        return await botClient.SendMessage(telegramId, message, replyMarkup: loreService.GetContentTip());
    }

    private async Task<Message> RetrieveAndSendLoreRecord(LoreRecord.CompositeId id, long telegramId)
    {
        var record = await loreRecordRepository.GetLoreRecord(id);
        return await record.MatchAsync(loreRecord => SendEditContentRequest(loreRecord, telegramId),
            () => loreMessageAssistant.SendFailedMessage(telegramId));
    }

    private async Task<Message> SendEditContentRequest(LoreRecord loreRecord, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.EditTemplate, loreRecord.Id.Key, loreRecord.Content) +
                      LoreService.GetLoreTag(ContentSuffix, loreRecord.Id.ChatId, loreRecord.Id.Key);
        return await botClient.SendMessage(telegramId, message, replyMarkup: loreService.GetContentTip(),
            cancellationToken: cancelToken.Token);
    }
}
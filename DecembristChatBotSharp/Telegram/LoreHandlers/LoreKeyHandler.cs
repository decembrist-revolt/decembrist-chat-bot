using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    CancellationTokenSource cancelToken,
    LoreRecordRepository loreRecordRepository,
    LoreMessageAssistant loreMessageAssistant)
{
    public async Task<Message> Do(string key, long lorChatId, long telegramId, LoreConfig loreConfig)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.AddLoreKey(key, lorChatId, telegramId);
        loreService.LogLore((uint) result, telegramId, lorChatId, key);
        return result switch
        {
            AddLoreKeyResult.Success => await SendRequestContent(key, lorChatId, telegramId, loreConfig),
            AddLoreKeyResult.Duplicate => await RetrieveAndSendLoreRecord((lorChatId, key), telegramId, loreConfig),
            AddLoreKeyResult.Limit => await loreMessageAssistant.SendHelpMessage(telegramId, loreConfig),
            AddLoreKeyResult.Failed => await loreMessageAssistant.SendFailedMessage(telegramId, loreConfig),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendRequestContent(string key, long lorChatId, long telegramId, LoreConfig loreConfig)
    {
        var message = string.Format(loreConfig.ContentRequest, LoreService.GetLoreTag(ContentSuffix, lorChatId, key));
        return await botClient.SendMessage(telegramId, message, replyMarkup: loreService.GetContentTip());
    }

    private async Task<Message> RetrieveAndSendLoreRecord(LoreRecord.CompositeId id, long telegramId, LoreConfig loreConfig)
    {
        var record = await loreRecordRepository.GetLoreRecord(id);
        return await record.MatchAsync(loreRecord => SendEditContentRequest(loreRecord, telegramId, loreConfig),
            () => loreMessageAssistant.SendFailedMessage(telegramId, loreConfig));
    }

    private async Task<Message> SendEditContentRequest(LoreRecord loreRecord, long telegramId, LoreConfig loreConfig)
    {
        var loreTag = LoreService.GetLoreTag(ContentSuffix, loreRecord.Id.ChatId, loreRecord.Id.Key);
        var message = string.Format(loreConfig.EditTemplate, loreRecord.Id.Key, loreRecord.Content) + loreTag;
        return await botClient.SendMessage(
            telegramId, message, replyMarkup: loreService.GetContentTip(), cancellationToken: cancelToken.Token);
    }
}
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class LoreService(
    LoreRecordRepository loreRecordRepository,
    AppConfig appConfig)
{
    public async Task<LoreResult> ChangeLoreContent(
        string key, string content, long loreChatId, long telegramId, DateTime date)
    {
        if (content.Length > appConfig.LoreConfig.ContentLimit) return LoreResult.Limit;
        if ((DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.ContentEditExpiration)
            return LoreResult.Expire;

        var isExist = await loreRecordRepository.IsLoreRecordExist((loreChatId, key.Trim()));
        if (!isExist) return LoreResult.NotFound;

        var isChange = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId, content);

        return isChange ? LoreResult.Success : LoreResult.Failed;
    }

    public async Task<LoreResult> AddLoreKey(string key, long loreChatId, long telegramId)
    {
        if (key.Length > appConfig.LoreConfig.KeyLimit) return LoreResult.Limit;

        var isExist = await loreRecordRepository.IsLoreRecordExist((loreChatId, key));
        if (isExist) return LoreResult.Duplicate;

        var isAdd = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId);
        return isAdd ? LoreResult.Success : LoreResult.Failed;
    }

    public async Task<LoreResult> ValidateKeyEdit(string key, long loreChatId) =>
        await loreRecordRepository.IsLoreRecordExist((loreChatId, key))
            ? LoreResult.Success
            : LoreResult.NotFound;

    public async Task<string> GetLoreRecord(long chatId, string key)
    {
        var id = (chatId, key);
        var maybeRecord = await loreRecordRepository.GetLoreRecord(id);
        return maybeRecord.Match(
            record => string.Format(appConfig.LoreConfig.ChatTemplate, record.Id.Key, record.Content),
            () => appConfig.LoreConfig.ChatFailed
        );
    }

    public ForceReplyMarkup GetContentTip() => new()
    {
        InputFieldPlaceholder = string.Format(appConfig.LoreConfig.Tip, appConfig.LoreConfig.ContentLimit),
    };

    public ForceReplyMarkup GetKeyTip() => new()
    {
        InputFieldPlaceholder = string.Format(appConfig.LoreConfig.Tip, appConfig.LoreConfig.KeyLimit),
    };

    public static string GetLoreTag(string suffix, long targetChatId, string key = "") =>
        $"\n{LoreReplyHandler.LoreTag}{suffix}:{key}:{targetChatId}";
}

public enum LoreResult
{
    Success,
    Duplicate,
    Expire,
    NotFound,
    Limit,
    Failed
}
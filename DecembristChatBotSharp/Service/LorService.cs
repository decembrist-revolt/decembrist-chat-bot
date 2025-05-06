using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class LorService(
    LorRecordRepository lorRecordRepository,
    AppConfig appConfig)
{
    public async Task<LorResult> HandleLorContent(
        string key, string content, long lorChatId, long telegramId, DateTime date)
    {
        if (content.Length > appConfig.LorConfig.ContentLimit) return LorResult.Limit;
        if ((DateTime.UtcNow - date).TotalMinutes > 2) return LorResult.Expire;

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key.Trim()));
        if (!isExist) return LorResult.NotFound;

        var isChange = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId, content);
        return isChange ? LorResult.Success : LorResult.Failed;
    }

    public async Task<LorResult> HandleLorKey(string key, long lorChatId, long telegramId)
    {
        if (key.Length > appConfig.LorConfig.KeyLimit) return LorResult.Limit;

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key));
        if (isExist) return LorResult.Duplicate;

        var isAdd = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId);
        return isAdd ? LorResult.Success : LorResult.Failed;
    }

    public async Task<LorResult> HandleLorKeyEdit(string key, long lorChatId) =>
        await lorRecordRepository.IsLorRecordExist((lorChatId, key))
            ? LorResult.Success
            : LorResult.NotFound;

    public async Task<string> GetLorRecord(LorRecord.CompositeId id)
    {
        var content = await lorRecordRepository.GetLorRecord(id);
        return content.Match(
            record => string.Format(appConfig.LorConfig.ChatTemplate, record.Id.Record, record.Content),
            () => appConfig.LorConfig.ChatFailed
        );
    }

    public ForceReplyMarkup GetContentTip() => new()
    {
        InputFieldPlaceholder = string.Format(appConfig.LorConfig.TipContent, appConfig.LorConfig.ContentLimit),
    };

    public ForceReplyMarkup GetKeyTip() => new()
    {
        InputFieldPlaceholder = string.Format(appConfig.LorConfig.TipKey, appConfig.LorConfig.KeyLimit),
    };

    public static string GetLorTag(string suffix, long targetChatId, string key = "") =>
        $"{LorReplyHandler.LorTag}{suffix}:{key}:{targetChatId}";
}

public enum LorResult
{
    Success,
    Duplicate,
    Expire,
    NotFound,
    Limit,
    Failed
}
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class LorService(
    LorRecordRepository lorRecordRepository,
    AppConfig appConfig)
{
    public async Task<LorResult> HandleLorContent(
        string key, string content, long lorChatId, long telegramId, DateTime date)
    {
        if (content.Length > appConfig.LorConfig.LorContentLimit) return LorResult.Limit;
        if ((DateTime.UtcNow - date).TotalMinutes > 2) return LorResult.Expire;

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key.Trim()));
        if (!isExist) return LorResult.NotFound;

        var isChange = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId, content);
        return isChange ? LorResult.Success : LorResult.Failed;
    }

    public async Task<LorResult> HandleLorKey(string key, long lorChatId, long telegramId)
    {
        if (key.Length > appConfig.LorConfig.LorKeyLimit) return LorResult.Limit;

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key));
        if (isExist) return LorResult.Duplicate;

        var isAdd = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId);
        return isAdd ? LorResult.Success : LorResult.Failed;
    }

    public async Task<LorResult> HandleLorKeyEdit(string key, long lorChatId) =>
        await lorRecordRepository.IsLorRecordExist((lorChatId, key))
            ? LorResult.Success
            : LorResult.NotFound;


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
using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using Lamar;
using Serilog;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class LoreService(
    LoreRecordRepository loreRecordRepository,
    AppConfig appConfig)
{
    public async Task<ChangeLoreContentResult> ChangeLoreContent(
        string key, string content, long loreChatId, long telegramId, DateTime date)
    {
        if (content.Length > appConfig.LoreConfig.ContentLimit) return ChangeLoreContentResult.Limit;
        if ((DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.ContentEditExpiration)
        {
            return ChangeLoreContentResult.Expire;
        }
    
        var isExist = await loreRecordRepository.IsLoreRecordExist((loreChatId, key.Trim()));
        if (!isExist) return ChangeLoreContentResult.NotFound;
    
        var isChange = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId, content);
    
        return isChange ? ChangeLoreContentResult.Success : ChangeLoreContentResult.Failed;
    }
    
    public async Task<AddLoreKeyResult> AddLoreKey(string key, long loreChatId, long telegramId)
    {
        if (key.Length > appConfig.LoreConfig.KeyLimit) return AddLoreKeyResult.Limit;
    
        var isExist = await loreRecordRepository.IsLoreRecordExist((loreChatId, key));
        if (isExist) return AddLoreKeyResult.Duplicate;
    
        var isAdd = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId);
        return isAdd ? AddLoreKeyResult.Success : AddLoreKeyResult.Failed;
    }
    
    public async Task<DeleteLoreRecordResult> DeleteLoreRecord(string key, long loreChatId, DateTime date)
    {
        if ((DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.DeleteExpiration)
            return DeleteLoreRecordResult.Expire;
    
        return await loreRecordRepository.DeleteLogRecord((loreChatId, key))
            ? DeleteLoreRecordResult.Success
            : DeleteLoreRecordResult.NotFound;
    }

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
        $"\n{LoreHandler.Tag}{suffix}:{key}:{targetChatId}";
    
    public void LogLore(uint result,
        long telegramId,
        long chatId,
        string key,
        string content = "-",
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        switch (result)
        {
            case 0:
                Log.Information("Lore operation SUCCESS: from: {0}, key: {1}, content:{2}, chat: {3}, by: {4}",
                    callerName, key, content, chatId, telegramId);
                break;
            case 1:
                Log.Error("Lore operation FAILED: reason: {0}, from: {1}, key: {2}, content:{3}, chat: {4}, by: {5}",
                    result, callerName, key, content, chatId, telegramId);
                break;
            default:
                Log.Information(
                    "Lore operation FAILED: reason: {0}, from: {1}, key: {2}, content:{3}, chat: {4}, by: {5}",
                    result, callerName, key, content, chatId, telegramId);
                break;
        }
    }
}

public enum ChangeLoreContentResult : uint
{
    Success = 0,
    Failed = 1,
    Expire,
    Limit,
    NotFound
}

public enum AddLoreKeyResult
{
    Success = 0,
    Failed = 1,
    Duplicate,
    Limit
}

public enum DeleteLoreRecordResult
{
    Success = 0,
    Expire,
    NotFound
}
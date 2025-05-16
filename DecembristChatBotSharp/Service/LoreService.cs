using System.Runtime.CompilerServices;
using System.Text;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using Lamar;
using Serilog;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class LoreService(
    LoreRecordRepository loreRecordRepository,
    MongoDatabase db,
    CancellationTokenSource cancelToken,
    AppConfig appConfig)
{
    private async Task<TResult> RunTransaction<TResult>(Func<IMongoSession, Task<TResult>> operation)
        where TResult : Enum
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var result = await operation(session);

        if (Convert.ToUInt32(result) != 0 || !await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
        }

        return result;
    }

    public async Task<ChangeLoreContentResult> ChangeLoreContent(
        string key, string content, long loreChatId, long telegramId, DateTime date) =>
        await RunTransaction(async session =>
        {
            if (content.Length > appConfig.LoreConfig.ContentLimit) return ChangeLoreContentResult.Limit;
            if (IsContentExpired(date)) return ChangeLoreContentResult.Expire;
            if (!await IsExist(key, loreChatId, session)) return ChangeLoreContentResult.NotFound;

            var isChange = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId, content, session);
            return isChange ? ChangeLoreContentResult.Success : ChangeLoreContentResult.Failed;
        });

    public async Task<AddLoreKeyResult> AddLoreKey(string key, long loreChatId, long telegramId) =>
        await RunTransaction(async session =>
        {
            if (key.Length > appConfig.LoreConfig.KeyLimit) return AddLoreKeyResult.Limit;
            if (await IsExist(key, loreChatId, session)) return AddLoreKeyResult.Duplicate;

            var isAdd = await loreRecordRepository.AddLoreRecord((loreChatId, key), telegramId, session: session);
            return isAdd ? AddLoreKeyResult.Success : AddLoreKeyResult.Failed;
        });

    public async Task<DeleteLoreRecordResult> DeleteLoreRecord(string key, long loreChatId, DateTime date)
    {
        if (IsDeletionExpired(date)) return DeleteLoreRecordResult.Expire;
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

    public async Task<Option<(string, int)>> GetLoreKeys(long chatId, int currentOffset = 0)
    {
        var maybeCount = await loreRecordRepository.GetKeysCount(chatId);
        return await maybeCount.MatchAsync(
            None: () => None,
            Some: async keysCount =>
            {
                if (keysCount < currentOffset) return None;
                var h = await FillLoreList(chatId, currentOffset);
                return h.Match(
                    x => Some((x, m: keysCount)),
                    () => None);
            });
    }

    private async Task<Option<string>> FillLoreList(long chatId, int currentOffset)
    {
        var maybeResult = await loreRecordRepository.GetLoreKeys(chatId, currentOffset);
        return maybeResult.Match(
            None: () => None,
            Some: keys =>
            {
                var sb = new StringBuilder();
                foreach (var key in keys)
                {
                    var escape = key.EscapeMarkdown();
                    sb.Append("• `").Append(escape).AppendLine("`");
                }

                return Some(sb.ToString());
            }
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

    private async Task<bool> IsExist(string key, long loreChatId, IMongoSession session) =>
        await loreRecordRepository.IsLoreRecordExist((loreChatId, key.Trim()), session);

    private bool IsContentExpired(DateTime date) =>
        (DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.ContentEditExpiration;

    private bool IsDeletionExpired(DateTime date) =>
        (DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.DeleteExpiration;

    public bool IsContainIndex(Map<string, string> parameters, out int currentOffset)
    {
        currentOffset = 0;
        return parameters.ContainsKey(CallbackService.IndexStartParameter) &&
               int.TryParse(parameters[CallbackService.IndexStartParameter], out currentOffset);
    }

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
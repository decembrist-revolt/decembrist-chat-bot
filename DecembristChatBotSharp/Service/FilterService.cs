using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class FilterService(
    MongoDatabase db,
    FilterRecordRepository filterRecordRepository,
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    public async Task<FilterCreateResult> HandleFilterRecord(string messageText, long targetChatId, DateTime date)
    {
        if (IsExpired(date)) return FilterCreateResult.Expire;
        using var session = await db.OpenSession();
        session.StartTransaction();

        if (await filterRecordRepository.IsFilterRecordExist((targetChatId, messageText), session))
            return FilterCreateResult.Duplicate;

        var isAdd = await filterRecordRepository.AddFilterRecord(new FilterRecord((targetChatId, messageText)),
            session);

        if (isAdd && await session.TryCommit(cancelToken.Token)) return FilterCreateResult.Success;

        await session.TryAbort(cancelToken.Token);
        return FilterCreateResult.Failed;
    }

    public async Task<FilterDeleteResult> DeleteFilterRecord(string messageText, long targetChatId, DateTime dateReply)
    {
        if (IsExpired(dateReply)) return FilterDeleteResult.Expire;
        using var session = await db.OpenSession();
        session.StartTransaction();

        if (!await filterRecordRepository.IsFilterRecordExist((targetChatId, messageText), session))
            return FilterDeleteResult.NotFound;

        var isDelete = await filterRecordRepository.DeleteFilterRecord((targetChatId, messageText), session);

        if (isDelete && await session.TryCommit(cancelToken.Token)) return FilterDeleteResult.Success;

        await session.TryAbort(cancelToken.Token);
        return FilterDeleteResult.Failed;
    }

    private bool IsExpired(DateTime date) =>
        (DateTime.UtcNow - date).TotalMinutes > appConfig.FilterConfig.ExpiredAddMinutes;

    public void LogFilter(byte result,
        long telegramId,
        long chatId,
        string record,
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        switch (result)
        {
            case 0:
                Log.Information("Filter operation SUCCESS: from: {0}, record: {1}, chat: {2}, by: {3}",
                    callerName, record, chatId, telegramId);
                break;
            case 1:
                Log.Error("Filter operation FAILED: reason: {0}, from: {1}, key: {2}, chat: {3}, by: {4}",
                    result, callerName, record, chatId, telegramId);
                break;
            default:
                Log.Information(
                    "Filter operation FAILED: reason: {0}, from: {1}, record: {2}, chat: {3}, by: {4}",
                    result, callerName, record, chatId, telegramId);
                break;
        }
    }
}

public enum FilterCreateResult : byte
{
    Success = 0,
    Failed = 1,
    Expire,
    Duplicate,
}

public enum FilterDeleteResult : byte
{
    Success = 0,
    Failed = 1,
    Expire,
    NotFound,
}
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class FilterService(MongoDatabase db, FilterRecordRepository filterRecordRepository, AppConfig appConfig)
{
    public async Task<FilterRecordResult> HandleFilterRecord(string messageText, long targetChatId, DateTime date)
    {
        if (IsExpired(date)) return FilterRecordResult.Expire;
        using var session = await db.OpenSession();
        session.StartTransaction();

        if (await filterRecordRepository.IsFilterRecordExist((targetChatId, messageText), session))
            return FilterRecordResult.Duplicate;

        await filterRecordRepository.AddFilterRecord(new FilterRecord((targetChatId, messageText)), session);
        return FilterRecordResult.Success;
    }

    private bool IsExpired(DateTime date) =>
        (DateTime.UtcNow - date).TotalMinutes > appConfig.LoreConfig.ContentEditExpiration;
}

public enum FilterRecordResult
{
    Success,
    Expire,
    Duplicate
}
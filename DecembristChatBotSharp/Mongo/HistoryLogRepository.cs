using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class HistoryLogRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<Unit> LogItem(
        long chatId,
        long telegramId,
        MemberItemType type,
        int count,
        MemberItemSourceType sourceType,
        IClientSessionHandle session,
        long? sourceTelegramId = null)
    {
        var collection = GetCollection<MemberItemHistoryLogData>();
        var id = CreateId<MemberItemHistoryLogData>(chatId, telegramId);
        var data = new MemberItemHistoryLogData(type, count, sourceType, sourceTelegramId);
        var log = new HistoryLog<MemberItemHistoryLogData>(id, HistoryLogType.MemberItem, data);

        return await collection.InsertOneAsync(session, log, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to log item {0} for {1}", type, telegramId));
    }

    private HistoryLog<T>.CompositeId CreateId<T>(long chatId, long telegramId) where T : IHistoryLogData =>
        new(chatId, telegramId, DateTime.UtcNow);

    private IMongoCollection<HistoryLog<T>> GetCollection<T>() where T : IHistoryLogData =>
        db.GetCollection<HistoryLog<T>>(nameof(HistoryLog<T>));
}
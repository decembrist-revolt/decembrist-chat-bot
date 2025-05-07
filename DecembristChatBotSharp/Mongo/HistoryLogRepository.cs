using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Scheduler;
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

    public async Task<Unit> LogItems(
        long chatId,
        Arr<long> telegramIds,
        MemberItemType type,
        int count,
        MemberItemSourceType sourceType,
        IClientSessionHandle session,
        long? sourceTelegramId = null)
    {
        var collection = GetCollection<MemberItemHistoryLogData>();

        var logs =
            from telegramId in telegramIds
            let id = CreateId<MemberItemHistoryLogData>(chatId, telegramId)
            let data = new MemberItemHistoryLogData(type, count, sourceType, sourceTelegramId)
            let log = new HistoryLog<MemberItemHistoryLogData>(id, HistoryLogType.MemberItem, data)
            select log;

        return await collection.InsertManyAsync(session, logs, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to log items {0} for {1}", type, telegramIds));
    }

    public async Task<Unit> LogTopLikers(
        long chatId,
        IEnumerable<TopLiker> topLikers,
        IClientSessionHandle session)
    {
        var collection = GetCollection<TopLikerHistoryLogData>();

        var logs =
            from topLiker in topLikers
            let id = CreateId<TopLikerHistoryLogData>(chatId, topLiker.TelegramId)
            let data = new TopLikerHistoryLogData(topLiker.LikesCount, topLiker.Position)
            let log = new HistoryLog<TopLikerHistoryLogData>(id, HistoryLogType.TopLiker, data)
            select log;

        return await collection.InsertManyAsync(session, logs, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to log top likers"));
    }
    
    public async Task<Unit> LogPremium(
        long chatId,
        long telegramId,
        PremiumMemberOperationType operationType,
        DateTime expirationDate,
        int level,
        IMongoSession session,
        long? sourceTelegramId = null,
        string? userProductId = null)
    {
        var collection = GetCollection<PremiumMemberHistoryLogData>();
        var id = CreateId<PremiumMemberHistoryLogData>(chatId, telegramId);
        var data = new PremiumMemberHistoryLogData(operationType, expirationDate, level, sourceTelegramId, userProductId);
        var log = new HistoryLog<PremiumMemberHistoryLogData>(id, HistoryLogType.PremiumMember, data);

        return await collection.InsertOneAsync(session, log, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to log premium member {0}", telegramId));
    }

    public async Task<Unit> LogResultDislikes(
        long chatId,
        long topDislikesUserId,
        long randomDislikerId,
        int dislikesCount,
        IMongoSession session)
    {
        var collection = GetCollection<DislikeResultHistoryLogData>();
        var id = CreateId<DislikeResultHistoryLogData>(chatId, topDislikesUserId);
        var data = new DislikeResultHistoryLogData(dislikesCount, randomDislikerId);
        var log = new HistoryLog<DislikeResultHistoryLogData>(id, HistoryLogType.TopDislikes, data);
        return await collection.InsertOneAsync(session, log, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .IfFail(ex => Log.Error(ex, "Failed to log dislikes result"));
    }

    private HistoryLog<T>.CompositeId CreateId<T>(long chatId, long telegramId) where T : IHistoryLogData =>
        new(chatId, telegramId, DateTime.UtcNow);

    private IMongoCollection<HistoryLog<T>> GetCollection<T>() where T : IHistoryLogData =>
        db.GetCollection<HistoryLog<T>>(nameof(HistoryLog<T>));
}
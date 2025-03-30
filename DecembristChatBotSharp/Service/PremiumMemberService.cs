using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class PremiumMemberService(
    PremiumMemberRepository premiumMemberRepository,
    HistoryLogRepository historyLogRepository,
    MongoDatabase db,
    CancellationTokenSource cancelToken
)
{
    public async Task<AddPremiumMemberResult> AddPremiumMember(
        long chatId,
        long telegramId,
        PremiumMemberOperationType operationType,
        DateTime expirationDate,
        int level = 1,
        IMongoSession? session = null,
        long? sourceTelegramId = null)
    {
        session ??= await db.OpenSession();
        session.StartTransaction();

        var premiumMember = new PremiumMember((telegramId, chatId), expirationDate, level);
        var addResult = await premiumMemberRepository.AddPremiumMember(premiumMember, session);
        if (addResult == AddPremiumMemberResult.Duplicate)
        {
            Log.Warning("Premium member {0} already exists in chat {1}", telegramId, chatId);
            await session.TryAbort(cancelToken.Token);
            return AddPremiumMemberResult.Duplicate;
        }

        if (addResult != AddPremiumMemberResult.Success)
        {
            await session.TryAbort(cancelToken.Token);
            return AddPremiumMemberResult.Error;
        }

        await historyLogRepository.LogPremium(
            chatId, telegramId, operationType, expirationDate, level, session, sourceTelegramId);

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit add premium member {0} in chat {1}", telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        Log.Information("Added premium member {0} to chat {1}", telegramId, chatId);
        return AddPremiumMemberResult.Success;
    }

    public async Task<bool> RemovePremiumMember(
        long chatId,
        long telegramId,
        PremiumMemberOperationType operationType,
        IMongoSession? session = null,
        long? sourceTelegramId = null)
    {
        session ??= await db.OpenSession();
        session.StartTransaction();

        var removeResult = await premiumMemberRepository.RemovePremiumMember((telegramId, chatId), session);
        if (!removeResult)
        {
            await session.TryAbort(cancelToken.Token);
            return false;
        }

        await historyLogRepository.LogPremium(
            chatId, telegramId, operationType, DateTime.UtcNow, 0, session, sourceTelegramId);

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit remove premium member {0} in chat {1}", telegramId, chatId);
            return false;
        }

        Log.Information("Removed premium member {0} from chat {1}", telegramId, chatId);
        return true;
    }
}
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
    /// <summary>
    /// Adds or updates a premium member in the database.
    /// If the member already exists, their expiration date is extended.
    /// </summary>
    /// <param name="chatId">The ID of the chat where the member is being added.</param>
    /// <param name="telegramId">The Telegram ID of the member.</param>
    /// <param name="operationType">The type of operation being performed (e.g., add or update).</param>
    /// <param name="expirationDate">The expiration date of the premium membership.</param>
    /// <param name="level">The level of the premium membership (default is 1).</param>
    /// <param name="session">The MongoDB session for the operation (optional).</param>
    /// <param name="sourceTelegramId">The Telegram ID of the source user (optional).</param>
    /// <returns>The result of the add operation.</returns>
    public async Task<AddPremiumMemberResult> AddPremiumMember(
        long chatId,
        long telegramId,
        PremiumMemberOperationType operationType,
        DateTime expirationDate,
        int level = 1,
        IMongoSession? session = null,
        long? sourceTelegramId = null)
    {
        if (session == null)
        {
            session = await db.OpenSession();
            session.StartTransaction();
        }

        CompositeId id = (telegramId, chatId);
        var getResult = await premiumMemberRepository.GetById(id, session);
        if (getResult.IsLeft)
        {
            await session.TryAbort(cancelToken.Token);
            var ex = getResult.IfRightThrow();
            Log.Error(ex, "Failed to get premium member {0} in chat {1}", telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        var maybeMember = getResult.IfLeftThrow();
        var member = maybeMember.Map(member => member with
        {
            ExpirationDate = expirationDate + (member.ExpirationDate - DateTime.UtcNow)
        }).IfNone(new PremiumMember((telegramId, chatId), expirationDate, level));
        var addResult = await premiumMemberRepository.AddPremiumMember(member, session);

        if (addResult == AddPremiumMemberResult.Error)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Warning("Failed to add premium member {0} to chat {1}", telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        await historyLogRepository.LogPremium(
            chatId, telegramId, operationType, expirationDate, level, session, sourceTelegramId);

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit {0} premium member {1} in chat {2}", addResult, telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        Log.Information(
            "{0} premium member {1} to chat {2} exp: {3}", addResult, telegramId, chatId, expirationDate);
        return addResult;
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

    public async Task<bool> IsPremium(long telegramId, long chatId) => 
        await premiumMemberRepository.IsPremium((telegramId, chatId));
}
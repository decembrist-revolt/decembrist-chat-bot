using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MemberItemService(
    AppConfig appConfig,
    MongoDatabase db,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    FastReplyRepository fastReplyRepository,
    ReactionSpamRepository reactionSpamRepository,
    CharmRepository charmRepository,
    RedditService redditService,
    Random random,
    TelegramPostService telegramPostService,
    CancellationTokenSource cancelToken)
{
    public async Task<(Option<MemberItemType>, OpenBoxResult)> OpenBox(long chatId, long telegramId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasBox = await memberItemRepository.RemoveMemberItem(chatId, telegramId, MemberItemType.Box, session);

        if (!hasBox)
        {
            await session.AbortTransactionAsync(cancelToken.Token);
            Log.Information("{0} tried to open non-existent box in chat {1}", telegramId, chatId);
            return (None, OpenBoxResult.NoItems);
        }

        await historyLogRepository.LogItem(
            chatId, telegramId, MemberItemType.Box, -1, MemberItemSourceType.Use, session);

        var itemType = GetRandomItem();

        var success = await memberItemRepository.AddMemberItem(chatId, telegramId, itemType, session);
        var isBox = itemType == MemberItemType.Box;
        if (isBox && success)
        {
            // If the item is a box, we need to add one more box
            success = await memberItemRepository.AddMemberItem(chatId, telegramId, MemberItemType.Box, session);
        }

        if (success)
        {
            var numberItems = isBox ? 2 : 1;
            await historyLogRepository.LogItem(
                chatId, telegramId, itemType, numberItems, MemberItemSourceType.Box, session);
            if (await session.TryCommit(cancelToken.Token))
            {
                Log.Information("{0} opened box and got {1} in chat {2}", telegramId, itemType, chatId);
                var result = numberItems == 2 ? OpenBoxResult.SuccessX2 : OpenBoxResult.Success;
                return (Some(itemType), result);
            }
        }

        await session.TryAbort(cancelToken.Token);
        Log.Error("Failed to open box for {0} in chat {1}", telegramId, chatId);
        return (None, OpenBoxResult.Failed);
    }

    public async Task<UseFastReplyResult> UseFastReply(long chatId, long telegramId, FastReply fastReply, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.FastReply, session);

        if (!hasItem) return await AbortSessionAndLog(UseFastReplyResult.NoItems, chatId, telegramId, session);

        await historyLogRepository.LogItem(
            chatId, telegramId, MemberItemType.FastReply, -1, MemberItemSourceType.Use, session);

        var addResult = await fastReplyRepository.AddFastReply(fastReply, session);

        switch (addResult)
        {
            case FastReplyRepository.InsertResult.Success when await session.TryCommit(cancelToken.Token):
                Log.Information("{0} used fast reply {1} in chat {2}", telegramId, fastReply.Id.Message, chatId);
                return UseFastReplyResult.Success;
            case FastReplyRepository.InsertResult.Success:
                await session.TryAbort(cancelToken.Token);
                Log.Error("{0} failed to commit use fast reply item for {1} in chat {2}", telegramId,
                    fastReply.Id.Message, chatId);
                return UseFastReplyResult.Failed;
            case FastReplyRepository.InsertResult.Failed:
                return await AbortSessionAndLog(UseFastReplyResult.Failed, chatId, telegramId,
                    reason: nameof(addResult), session);
            case FastReplyRepository.InsertResult.Duplicate:
                return await AbortSessionAndLog(UseFastReplyResult.Duplicate, chatId, telegramId, session);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public async Task<UseRedditMemeResult> UseRedditMeme(long chatId, long telegramId, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.RedditMeme, session);

        if (!hasItem)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("{0} tried to use non-existent reddit meme in chat {1}", telegramId, chatId);
            return new UseRedditMemeResult(None, UseRedditMemeResult.Type.NoItems);
        }

        var maybeMeme = await redditService.GetRandomMeme();
        if (maybeMeme.IsNone)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to get random reddit meme for {0} in chat {1}", telegramId, chatId);
            return new UseRedditMemeResult(None, UseRedditMemeResult.Type.Failed);
        }

        await historyLogRepository.LogItem(
            chatId, telegramId, MemberItemType.RedditMeme, -1, MemberItemSourceType.Use, session);

        return await maybeMeme.MatchAsync(async meme =>
        {
            if (await session.TryCommit(cancelToken.Token))
            {
                Log.Information("{0} used reddit meme in chat {1}", telegramId, chatId);
                return new UseRedditMemeResult(meme, UseRedditMemeResult.Type.Success);
            }

            Log.Error("Failed to commit use reddit meme item for {0} in chat {1}", telegramId, chatId);
            return new UseRedditMemeResult(None, UseRedditMemeResult.Type.Failed);
        }, async () =>
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to get random reddit meme for {0} in chat {1}", telegramId, chatId);
            return new UseRedditMemeResult(None, UseRedditMemeResult.Type.Failed);
        });
    }

    public async Task<UseTelegramMemeResult> UseTelegramMeme(long chatId, long telegramId, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.TelegramMeme, session);

        if (!hasItem)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("{0} tried to use non-existent telegram meme in chat {1}", telegramId, chatId);
            return new UseTelegramMemeResult(None, UseTelegramMemeResult.Type.NoItems);
        }

        var maybeMeme = await telegramPostService.GetRandomPostPicture();
        if (maybeMeme.IsNone)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to get random telegram meme for {0} in chat {1}", telegramId, chatId);
            return new UseTelegramMemeResult(None, UseTelegramMemeResult.Type.Failed);
        }

        await historyLogRepository.LogItem(
            chatId, telegramId, MemberItemType.TelegramMeme, -1, MemberItemSourceType.Use, session);

        return await maybeMeme.MatchAsync(async meme =>
        {
            if (await session.TryCommit(cancelToken.Token))
            {
                Log.Information("{0} used reddit meme in chat {1}", telegramId, chatId);
                return new UseTelegramMemeResult(meme, UseTelegramMemeResult.Type.Success);
            }

            Log.Error("Failed to commit use reddit meme item for {0} in chat {1}", telegramId, chatId);
            return new UseTelegramMemeResult(None, UseTelegramMemeResult.Type.Failed);
        }, async () =>
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to get random reddit meme for {0} in chat {1}", telegramId, chatId);
            return new UseTelegramMemeResult(None, UseTelegramMemeResult.Type.Failed);
        });
    }

    public async Task<bool> GiveItem(long chatId, long telegramId, long adminTelegramId, MemberItemType type)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var success = await memberItemRepository.AddMemberItem(chatId, telegramId, type, session);

        if (!success)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("{0} failed to give {1} to {2} in chat {3}", adminTelegramId, type, telegramId, chatId);
            return false;
        }

        await historyLogRepository.LogItem(
            chatId, telegramId, type, 1, MemberItemSourceType.Admin, session, adminTelegramId);

        if (await session.TryCommit(cancelToken.Token))
        {
            Log.Information("{0} gave {1} to {2} in chat {3}", adminTelegramId, type, telegramId, chatId);
            return true;
        }

        await session.TryAbort(cancelToken.Token);
        Log.Error("{0} failed to commit give {1} to {2} in chat {3}", adminTelegramId, type, telegramId, chatId);
        return false;
    }

    public async Task<CurseResult> UseCurse(
        long chatId, long telegramId, ReactionSpamMember member, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.Curse, session);
        var maybeResult = !hasItem ? CurseResult.NoItems : await HandleItem(session);

        return maybeResult switch
        {
            CurseResult.Success or CurseResult.Blocked => await HandleSuccessUsingItem(maybeResult, CurseResult.Failed,
                session, chatId, telegramId),
            CurseResult.Failed => await AbortSessionAndLog(CurseResult.Failed, chatId, telegramId,
                reason: nameof(CurseResult.Failed), session),
            CurseResult.NoItems or CurseResult.Duplicate => await AbortSessionAndLog(maybeResult, chatId, telegramId,
                session),
            _ => throw new ArgumentOutOfRangeException()
        };

        async Task<CurseResult> HandleItem(IMongoSession localSession)
        {
            var targetHasAmulet = await CheckAmulet(chatId, member.Id.TelegramId, localSession);
            await historyLogRepository.LogItem(
                chatId, telegramId, MemberItemType.Curse, -1, MemberItemSourceType.Use, localSession);
            return targetHasAmulet
                ? CurseResult.Blocked
                : await reactionSpamRepository.AddReactionSpamMember(member, localSession);
        }
    }

    public async Task<CharmResult> UseCharm(
        long chatId, long telegramId, CharmMember member, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.Charm, session);

        var maybeResult = !hasItem ? CharmResult.NoItems : await HandleCharm(session);

        return maybeResult switch
        {
            CharmResult.Success or CharmResult.Blocked =>
                await HandleSuccessUsingItem(maybeResult, CharmResult.Failed, session, chatId, telegramId),
            CharmResult.Failed => await AbortSessionAndLog(maybeResult, chatId, telegramId,
                reason: nameof(CharmResult.Failed), session),
            CharmResult.NoItems or CharmResult.Duplicate =>
                await AbortSessionAndLog(maybeResult, chatId, telegramId, session),
            _ => throw new ArgumentOutOfRangeException()
        };

        async Task<CharmResult> HandleCharm(IMongoSession localSession)
        {
            var targetHasAmulet = await CheckAmulet(chatId, member.Id.TelegramId, localSession);
            await historyLogRepository.LogItem(
                chatId, telegramId, MemberItemType.Charm, -1, MemberItemSourceType.Use, localSession);
            return targetHasAmulet
                ? CharmResult.Blocked
                : await charmRepository.AddCharmMember(member, localSession);
        }
    }

    async Task<T> HandleSuccessUsingItem<T>(
        T result,
        T failed,
        IMongoSession session,
        long chatId,
        long telegramId,
        [CallerMemberName] string callerName = "unknownCaller") where T : Enum =>
        await session.TryCommit(cancelToken.Token)
            ? result.LogSuccessUsingItem(chatId, telegramId, callerName)
            : await AbortSessionAndLog(failed, chatId, telegramId, "Failed to commit", session, callerName);


    private async Task<T> AbortSessionAndLog<T>(
        T maybeResult,
        long chatId,
        long telegramId,
        string reason,
        IMongoSession session,
        [CallerMemberName] string callerName = "unknownCaller") where T : Enum
    {
        await session.TryAbort(cancelToken.Token);
        Log.Error("Item usage failed from: {0}, User: {1}, Chat: {2}, Reason: {3}",
            callerName, telegramId, chatId, reason);
        return maybeResult;
    }

    private async Task<T> AbortSessionAndLog<T>(
        T maybeResult,
        long chatId,
        long telegramId,
        IMongoSession session,
        [CallerMemberName] string callerName = "unknownCaller") where T : Enum
    {
        await session.TryAbort(cancelToken.Token);
        Log.Information("Item usage failed from: {0}, User: {1}, Chat: {2}, Reason: {3}",
            callerName, telegramId, chatId, maybeResult);
        return maybeResult;
    }


    private MemberItemType GetRandomItem()
    {
        var itemChances = appConfig.ItemConfig.ItemChance;
        var total = itemChances.Values.Sum();
        var roll = random.NextDouble() * total;

        var cumulative = 0.0;

        foreach (var pair in itemChances)
        {
            cumulative += pair.Value;
            if (roll <= cumulative)
            {
                return pair.Key;
            }
        }

        return itemChances.Keys.First();
    }

    private async Task<bool> CheckAmulet(long chatId, long receiverId, IMongoSession session)
    {
        var hasAmulet = await memberItemRepository.RemoveMemberItem(chatId, receiverId, MemberItemType.Amulet, session);
        if (hasAmulet)
            await historyLogRepository.LogItem(chatId, receiverId, MemberItemType.Amulet, -1,
                MemberItemSourceType.Use, session);
        return hasAmulet;
    }
}

public enum OpenBoxResult
{
    SuccessX2,
    Success,
    NoItems,
    Failed
}

public enum UseFastReplyResult
{
    Success,
    NoItems,
    Duplicate,
    Failed
}

public enum CurseResult
{
    Success,
    Blocked,
    NoItems,
    Duplicate,
    Failed
}

public enum CharmResult
{
    Success,
    Blocked,
    NoItems,
    Duplicate,
    Failed
}

public record struct UseRedditMemeResult(Option<RedditRandomMeme> Meme, UseRedditMemeResult.Type Result)
{
    public enum Type
    {
        Success,
        NoItems,
        Failed
    }
}

public record struct UseTelegramMemeResult(Option<TelegramRandomMeme> Meme, UseTelegramMemeResult.Type Result)
{
    public enum Type
    {
        Success,
        NoItems,
        Failed
    }
}
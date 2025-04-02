using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
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
    RedditService redditService,
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

        if (success)
        {
            await historyLogRepository.LogItem(
                chatId, telegramId, itemType, 1, MemberItemSourceType.Box, session);
            if (await session.TryCommit(cancelToken.Token))
            {
                Log.Information("{0} opened box and got {1} in chat {2}", telegramId, itemType, chatId);
                return (Some(itemType), OpenBoxResult.Success);
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

        if (!hasItem)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("{0} tried to use non-existent fast reply in chat {1}", telegramId, chatId);
            return UseFastReplyResult.NoItems;
        }

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
                await session.TryAbort(cancelToken.Token);
                Log.Error("Failed to use fast reply for {0} in chat {1}", telegramId, chatId);
                return UseFastReplyResult.Failed;
            case FastReplyRepository.InsertResult.Duplicate:
                await session.TryAbort(cancelToken.Token);
                Log.Information("{0} tried to use duplicate fast reply in chat {1}", telegramId, chatId);
                return UseFastReplyResult.Duplicate;
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

    public async Task<ReactionSpamResult> UseReactionSpam(
        long chatId, long telegramId, ReactionSpamMember member, bool isAdmin)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var hasItem = isAdmin || await memberItemRepository
            .RemoveMemberItem(chatId, telegramId, MemberItemType.Curse, session);
        if (!hasItem)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("{0} tried to use non-existent reaction spam in chat {1}", telegramId, chatId);
            return ReactionSpamResult.NoItems;
        }

        var maybeResult = await reactionSpamRepository.AddReactionSpamMember(member, session);

        await historyLogRepository.LogItem(
            chatId, telegramId, MemberItemType.Curse, -1, MemberItemSourceType.Use, session);
        switch (maybeResult)
        {
            case ReactionSpamResult.Success when await session.TryCommit(cancelToken.Token):
                Log.Information("{0} used reaction spam in chat {1}", telegramId, chatId);
                return ReactionSpamResult.Success;
            case ReactionSpamResult.Success:
                await session.TryAbort(cancelToken.Token);
                Log.Error("{0} failed to commit use reaction spam item in chat {1}", telegramId, chatId);
                return ReactionSpamResult.Failed;
            case ReactionSpamResult.Failed:
                await session.TryAbort(cancelToken.Token);
                Log.Error("Failed to use reaction spam item for {0} in chat {1}", telegramId, chatId);
                return ReactionSpamResult.Failed;
            case ReactionSpamResult.Duplicate:
                await session.TryAbort(cancelToken.Token);
                Log.Information("{0} tried to use duplicate fast reply in chat {1}", telegramId, chatId);
                return ReactionSpamResult.Duplicate;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private MemberItemType GetRandomItem()
    {
        var random = new Random();

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
}

public enum OpenBoxResult
{
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

public enum ReactionSpamResult 
{
    Success,
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
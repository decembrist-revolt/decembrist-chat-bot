using System.Text;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using MongoDB.Driver;
using Quartz;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Scheduler;

public readonly record struct TopLiker(int Position, long TelegramId, string Username, int LikesCount);

[Singleton]
public class DailyTopLikersGiftJob(
    MongoDatabase db,
    AppConfig appConfig,
    MemberLikeRepository memberLikeRepository,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var jobKey = new JobKey(nameof(DailyTopLikersGiftJob));
        var triggerKey = new TriggerKey(nameof(DailyTopLikersGiftJob));

        var job = JobBuilder.Create<DailyTopLikersGiftJob>()
            .WithIdentity(jobKey)
            .Build();

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithCronSchedule(
                appConfig.CommandConfig.LikeConfig.DailyTopLikersGiftCronUtc,
                x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, newTrigger);
        }
        else
        {
            await scheduler.ScheduleJob(job, newTrigger);
        }
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var chatIds = await memberLikeRepository.GetChatIds();
        if (chatIds.IsEmpty)
        {
            Log.Information("No chats found for daily top likers gift");
            return;
        }

        foreach (var chatId in chatIds)
        {
            await HandleChatTopLikers(chatId);
        }
    }

    private async Task HandleChatTopLikers(long chatId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var topLikers = await GetTopLikers(chatId, session);
        if (topLikers.IsEmpty)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("No top likers found for chat {0}", chatId);
            return;
        }

        var telegramIds = topLikers.Select(liker => liker.TelegramId).ToArr()!;
        var likesCount = topLikers.Sum(liker => liker.LikesCount);
        var removedCount = await memberLikeRepository.RemoveAllInChat(chatId, session);
        if (removedCount < likesCount)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to remove top likers for chat {0}", chatId);
            return;
        }

        var addCount = await memberItemRepository.AddMemberItems(chatId, telegramIds, MemberItemType.Box, session);
        if (addCount != telegramIds.Length)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to add items to top likers for chat {0}", chatId);
            return;
        }

        await LogEvents(chatId, telegramIds, topLikers, session);

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit top likers for chat {0}", chatId);
            return;
        }

        if (!await SendTopLikersGiftMessage(chatId, topLikers))
        {
            Log.Error("Failed to send top likers gift message for chat {0}", chatId);
            return;
        }

        Log.Information("Successfully processed top likers for chat {0}", chatId);
    }

    private async Task<Unit> LogEvents(
        long chatId, Arr<long> telegramIds, Arr<TopLiker> topLikers, IMongoSession session)
    {
        await historyLogRepository.LogItems(
            chatId, telegramIds, MemberItemType.Box, 1, MemberItemSourceType.TopLiker, session);
        await Task.Delay(1, cancelToken.Token);
        return await historyLogRepository.LogTopLikers(chatId, topLikers, session);
    }

    private async Task<Arr<TopLiker>> GetTopLikers(long chatId, IMongoSession session)
    {
        var likersCount = appConfig.CommandConfig.LikeConfig.DailyTopLikersCount;
        var maybeTopLikers = (await memberLikeRepository.GetTopLikeMembers(chatId, likersCount, session))
            .Where(liker => liker.Count > 1)
            .Select((liker, idx) =>
                from username in botClient.GetUsername(chatId, liker.LikeTelegramId, cancelToken.Token)
                    .ToAsync()
                    .IfNone(liker.LikeTelegramId.ToString)
                select new TopLiker(idx + 1, liker.LikeTelegramId, username, liker.Count));

        return (await maybeTopLikers.TraverseSerial(identity)).ToArr();
    }

    private async Task<bool> SendTopLikersGiftMessage(long chatId, IEnumerable<TopLiker> topLikers)
    {
        var likersCount = appConfig.CommandConfig.LikeConfig.DailyTopLikersCount;
        var builder = new StringBuilder();
        builder.AppendLine("#  Username - Likes");
        var likersText = topLikers.Fold(builder, (acc, liker) =>
            acc.AppendLine($"#{liker.Position}. {liker.Username} - {liker.LikesCount}"))!;
        var giftMessage = appConfig.CommandConfig.LikeConfig.TopLikersGiftMessage;
        var message = string.Format(giftMessage, likersText, likersCount);

        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Sent top likers gift message to chat {0}", chatId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to send top likers gift message to chat {0}", chatId);
                    return false;
                });
    }
}
﻿using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class DailyDislikesResultsJob(
    AppConfig appConfig,
    MongoDatabase db,
    DislikeRepository dislikeRepository,
    MemberItemRepository memberItemRepository,
    ReactionSpamRepository reactionSpamRepository,
    HistoryLogRepository historyLogRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    private static readonly Random Random = new();

    public async Task Register(IScheduler scheduler)
    {
        var jobKey = new JobKey(nameof(DailyDislikesResultsJob));
        var triggerKey = new TriggerKey(nameof(DailyDislikesResultsJob));

        var job = JobBuilder.Create<DailyDislikesResultsJob>()
            .WithIdentity(jobKey)
            .Build();

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithCronSchedule(
                appConfig.DislikeConfig.DailyResultCronUtc,
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
        var chatIds = await dislikeRepository.GetChatIds();
        if (chatIds.IsEmpty)
        {
            Log.Information("No chats found for daily reward dislikes");
            return;
        }

        foreach (var chatId in chatIds)
        {
            await HandleDislikeResults(chatId);
        }
    }

    private async Task<Unit> HandleDislikeResults(long chatId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        return await dislikeRepository.GetDislikeTopResults(chatId, session)
            .MatchAsync(
                None: async () => await AbortSessionAndLog("No dislike results found for chat {0}", chatId, session),
                Some: async group => await HandleDislikeGroup(group, chatId, session));
    }

    private async Task<Unit> HandleDislikeGroup(DislikesResultGroup group, long chatId, IMongoSession session)
    {
        var (topDislikesUserId, dislikers) = (group.DislikeUserId, group.Dislikers);
        var insertResult = await AddCurseTopDislikeUser(chatId, topDislikesUserId, session);

        if (insertResult != ReactionSpamResult.Success)
            return await AbortSessionWrapper("Failed to add curse to the disliked user for chat {0}");

        if (dislikers.Length == 0)
            return await AbortSessionWrapper("Failed to get dislikers array for chat {0}");

        var randomDislikerId = dislikers[Random.Next(0, dislikers.Length)];
        var isAddItem = await memberItemRepository.AddMemberItem(chatId, randomDislikerId, MemberItemType.Box, session);

        if (!isAddItem)
            return await AbortSessionWrapper("Failed to add item to random disliker for chat {0}");

        if (!await dislikeRepository.RemoveAllInChat(chatId, session))
            return await AbortSessionWrapper("Failed to remove dislikes for chat {0}");

        await historyLogRepository.LogResultDislikes(chatId, topDislikesUserId, randomDislikerId,
            dislikers.Length(), session);

        if (!await session.TryCommit(cancelToken.Token))
            return await AbortSessionWrapper("Failed to commit the dislike repository for chat {0}");

        if (!await SendDailyResultMessage(chatId, topDislikesUserId, randomDislikerId))
            Log.Error("Failed to send  dislikes result message for chat {0}", chatId);
        else
            Log.Information("Successfully processed dislikes results for chat {0}", chatId);

        return unit;

        async Task<Unit> AbortSessionWrapper(string template) =>
            await AbortSessionAndLog(template, chatId, session);
    }

    private async Task<Unit> AbortSessionAndLog(string messageTemplate, long chatId, IMongoSession session)
    {
        await session.TryAbort(cancelToken.Token);
        Log.Error(messageTemplate, chatId);
        return unit;
    }

    private async Task<ReactionSpamResult> AddCurseTopDislikeUser(long chatId, long topDislikesUserId,
        IMongoSession session)
    {
        var emoji = new ReactionTypeEmoji();
        emoji.Emoji = appConfig.DislikeConfig.DailyResultEmoji;
        var expireAt = DateTime.UtcNow.AddMinutes(appConfig.DislikeConfig.EmojiDurationMinutes);
        var curseMember = new ReactionSpamMember((topDislikesUserId, chatId), emoji, expireAt);
        await reactionSpamRepository.DeleteReactionSpamMember(curseMember.Id, session);
        var result = await reactionSpamRepository.AddReactionSpamMember(curseMember, session);
        return result;
    }

    private async Task<bool> SendDailyResultMessage(long chatId, long dislikeUserId, long randomDislikerId)
    {
        var usernames =
            (from telegramId in new[] { dislikeUserId, randomDislikerId }
                select botClient.GetUsername(chatId, telegramId, cancelToken.Token)
                    .ToAsync()
                    .IfNone(telegramId.ToString)).ToArr();
        var usernamesString = (await usernames.Traverse(identity)).ToFullString();
        var message = string.Format(appConfig.DislikeConfig.DailyResultMessage, usernamesString,
            appConfig.DislikeConfig.DailyResultEmoji);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Send dislike result message in chat {0}", chatId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed send dislike result message in chat {0}", chatId);
                    return false;
                });
    }
}
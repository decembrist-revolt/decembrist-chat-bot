using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class DailyPremiumRewardJob(
    MongoDatabase db,
    AppConfig appConfig,
    PremiumMemberRepository premiumMemberRepository,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var jobKey = new JobKey(nameof(DailyPremiumRewardJob));
        var triggerKey = new TriggerKey(nameof(DailyPremiumRewardJob));

        var job = JobBuilder.Create<DailyPremiumRewardJob>()
            .WithIdentity(jobKey)
            .Build();

        var newTrigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithCronSchedule(
                appConfig.CommandAssistanceConfig.PremiumConfig.DailyPremiumRewardCronUtc,
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
        var chatIds = await premiumMemberRepository.GetChatIds();
        if (chatIds.IsEmpty)
        {
            Log.Information("No chats found for daily premium rewards");
            return;
        }

        foreach (var chatId in chatIds)
        {
            await HandleChatRewards(chatId);
        }
    }

    private async Task HandleChatRewards(long chatId)
    {
        using var session = await db.OpenSession();
        session.StartTransaction();

        var telegramIds = await premiumMemberRepository.GetTelegramIds(chatId);
        if (telegramIds.IsEmpty)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Warning("No premium found for chat {0}", chatId);
            return;
        }

        var addCount = await memberItemRepository.AddMemberItems(chatId, telegramIds, MemberItemType.Box, session);
        if (addCount != telegramIds.Length)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to add premium items for chat {0}", chatId);
            return;
        }

        await historyLogRepository.LogItems(
            chatId, telegramIds, MemberItemType.Box, 1, MemberItemSourceType.PremiumDaily, session);

        if (!await session.TryCommit(cancelToken.Token))
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to commit premium items for chat {0}", chatId);
            return;
        }

        if (!await SendPremiumRewardMessage(chatId, telegramIds)) return;

        Log.Information("Successfully processed premium rewards for chat {0}", chatId);
    }

    private async Task<bool> SendPremiumRewardMessage(long chatId, Arr<long> telegramIds)
    {
        var usernames =
            from telegramId in telegramIds
            select botClient.GetUsername(chatId, telegramId, cancelToken.Token)
                .ToAsync()
                .IfNone(telegramId.ToString);
        var usernamesString = (await usernames.Traverse(identity)).Map(username => username.EscapeMarkdown()).ToFullString();

        var message = string.Format(appConfig.CommandAssistanceConfig.PremiumConfig.DailyPremiumRewardMessage, usernamesString);
        return await botClient.SendMessage(
                chatId, message, parseMode: ParseMode.MarkdownV2, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Sent premium reward message to chat {0}", chatId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to send premium reward message to chat {0}", chatId);
                    return false;
                });
    }
}
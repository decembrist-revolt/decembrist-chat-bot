using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class CheckCaptchaJob(
    BotClient bot,
    AppConfig appConfig,
    NewMemberRepository db,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var job = JobBuilder.Create<CheckCaptchaJob>()
            .WithIdentity(nameof(CheckCaptchaJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(nameof(CheckCaptchaJob))
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(appConfig.CheckCaptchaIntervalSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var olderThanUtc = DateTime.UtcNow.AddSeconds(-appConfig.CaptchaTimeSeconds);
        var members = await db.GetNewMembers(olderThanUtc)
            .Match(identity, OnGetMembersFailed);
        
        await members.Select(HandleExpiredMember).WhenAll();
    }
    
    private List<NewMember> OnGetMembersFailed(Exception ex)
    {
        Log.Error(ex, "Failed to get new members");
        return [];
    }

    private async Task<Unit> HandleExpiredMember(NewMember newMember)
    {
        var (id, username, welcomeMessageId, _, _) = newMember;
        var (telegramId, chatId) = id;

        await Task.WhenAll(
            BanMember(chatId, telegramId, username),
            DeleteWelcomeMessage(chatId, welcomeMessageId, username)
        );

        return unit;
    }

    private async Task<Unit> BanMember(long chatId, long telegramId, string username)
    {
        var result = await TryAsync(bot.BanChatMember(
            chatId: chatId,
            userId: telegramId,
            untilDate: DateTime.UtcNow.AddSeconds(0)
        ).UnitTask);

        return result.Match(
            _ => OnBannedUser(telegramId, username, chatId),
            ex => Log.Error(ex, "Failed to ban user {0} in chat {1}", telegramId, chatId)
        );
    }

    private Unit OnBannedUser(long telegramId, string username, long chatId)
    {
        Log.Information("User {0} banned cause bad captcha in chat {1}", username, chatId);

        return db.RemoveNewMember((telegramId, chatId)).Match(
            removed => OnRemoveSuccess(removed, telegramId),
            ex => Log.Error(ex, "Failed to remove user {0} from new members", telegramId)).Ignore();
    }

    private void OnRemoveSuccess(bool removed, long telegramId)
    {
        if (removed)
        {
            Log.Information("User {0} removed from new members", telegramId);
        }
        else
        {
            Log.Error("Failed to remove user {0} from new members", telegramId);
        }
    }

    private async Task<Unit> DeleteWelcomeMessage(long chatId, int welcomeMessageId, string username)
    {
        var result = await TryAsync(bot.DeleteMessage(chatId, welcomeMessageId).UnitTask);
        return result.Match(
            Succ: _ => Log.Information("Deleted welcome message for user {0} in chat {1}", username, chatId),
            Fail: ex => Log.Error(ex, "Failed to delete welcome message for user {0} in chat {1}", username, chatId)
        );
    }
}
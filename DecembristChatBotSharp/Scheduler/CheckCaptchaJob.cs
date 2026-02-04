using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class CheckCaptchaJob(
    BotClient bot,
    AppConfig appConfig,
    BanService banService,
    MessageAssistance messageAssistance,
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
                .WithIntervalInHours(appConfig.CaptchaJobConfig.CheckCaptchaIntervalHours)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var olderThanUtc = DateTime.UtcNow.AddHours(-appConfig.CaptchaJobConfig.CaptchaTimeHours);
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
        var (telegramId, chatId) = newMember.Id;
        return await Array(banService.KickChatMember(chatId, telegramId),
            messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(CheckCaptchaJob)),
            db.RemoveNewMember(newMember.Id).UnitTask()
        ).WhenAll();
    }
}
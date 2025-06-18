using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Quartz;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class CheckBlackListCaptchaJob(
    BotClient bot,
    AppConfig appConfig,
    BanService banService,
    MessageAssistance messageAssistance,
    SuspiciousMessageRepository db,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var job = JobBuilder.Create<CheckBlackListCaptchaJob>()
            .WithIdentity(nameof(CheckBlackListCaptchaJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(nameof(CheckBlackListCaptchaJob))
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(appConfig.BlackListConfig.CheckCaptchaIntervalSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var olderThanUtc = DateTime.UtcNow.AddSeconds(-appConfig.BlackListConfig.CaptchaTimeSeconds);
        var members = await db.GetExpiredMessages(olderThanUtc);

        await members.Select(HandleExpiredMember).WhenAll();
    }

    private async Task<Unit> HandleExpiredMember(SuspiciousMessage message) => await Array(
        messageAssistance.DeleteCommandMessage(message.Id.ChatId, message.CaptchaMessageId, nameof(CheckCaptchaJob)),
        messageAssistance.DeleteCommandMessage(message.Id.ChatId, message.Id.MessageId, nameof(CheckCaptchaJob)),
        db.DeleteSuspiciousMessage(message.Id).UnitTask()).WhenAll();
}
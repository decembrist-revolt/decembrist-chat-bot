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
    FilteredMessageRepository db,
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
                .WithIntervalInSeconds(appConfig.FilterConfig.CheckCaptchaIntervalSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var olderThanUtc = DateTime.UtcNow.AddSeconds(-appConfig.FilterConfig.CaptchaTimeSeconds);
        var members = await db.GetExpiredMessages(olderThanUtc);

        await members.Select(HandleExpiredMember).WhenAll();
    }

    private async Task<Unit> HandleExpiredMember(FilteredMessage message) => await Array(
        messageAssistance.DeleteCommandMessage(message.Id.ChatId, message.CaptchaMessageId, nameof(CheckCaptchaJob)),
        messageAssistance.DeleteCommandMessage(message.Id.ChatId, message.Id.MessageId, nameof(CheckCaptchaJob)),
        db.DeleteFilteredMessage(message.Id).UnitTask()).WhenAll();
}
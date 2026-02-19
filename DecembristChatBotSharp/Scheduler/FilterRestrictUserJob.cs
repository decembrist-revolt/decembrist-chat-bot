using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Quartz;
using Serilog;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class FilterRestrictUserJob(
    AppConfig appConfig,
    BanService banService,
    MessageAssistance messageAssistance,
    FilterRestrictUserRepository db,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var job = JobBuilder.Create<FilterRestrictUserJob>()
            .WithIdentity(nameof(FilterRestrictUserJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(nameof(FilterRestrictUserJob))
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(appConfig.FilterJobConfig.CheckFilterRestrictSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var users = await db.GetUsersExpired();
        if (users.Count <= 0) return;
        await users.Select(user =>
        {
            var (userId, chatId) = user.Id;
            Log.Information("Restrict for User: {userId} expired in chat: {chatId}", userId, chatId);
            return Array(banService.UnRestrictChatMember(chatId, userId),
                messageAssistance.DeleteCommandMessage(chatId, user.RestrictMessageId, nameof(FilterRestrictUserJob))
            ).WhenAll();
        }).WhenAll();
        await db.DeleteUsers(users);
    }
}
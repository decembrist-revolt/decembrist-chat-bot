using DecembristChatBotSharp.Mongo;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class ExpiredMessageJob(
    AppConfig appConfig,
    ExpiredMessageRepository repository,
    BotClient botClient,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var job = JobBuilder.Create<ExpiredMessageJob>()
            .WithIdentity(nameof(ExpiredMessageJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(nameof(ExpiredMessageJob))
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(appConfig.CommandAssistanceConfig.CommandIntervalSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var messages = await repository.GetExpiredMessages();
        var chatIdToMessageIds = messages
            .GroupBy(message => message.Id.ChatId, message => message.Id.MessageId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        await chatIdToMessageIds.Map(group => DeleteMessages(group.Key, group.Value)).WhenAll();
        await repository.DeleteMessages(messages);
    }

    private async Task<Unit> DeleteMessages(long chatId, int[] messageIds) =>
        await botClient.DeleteMessages(chatId, messageIds, cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => Log.Information("Deleted expired message {0} in chat {1}", messageIds, chatId),
                ex => Log.Error(ex, "Failed to delete expired message {0} in chat {1}", messageIds, chatId)
            );
}
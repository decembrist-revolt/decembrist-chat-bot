using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService) : IRegisterJob
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
                .WithIntervalInSeconds(appConfig.FilterJobConfig.CheckCaptchaIntervalSeconds)
                .RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var olderThanUtc = DateTime.UtcNow.AddSeconds(-appConfig.FilterJobConfig.CaptchaTimeSeconds);
        var members = await db.GetExpiredMessages(olderThanUtc);

        await members.Select(HandleExpiredMember).WhenAll();
    }

    private async Task<Unit> HandleExpiredMember(FilteredMessage message)
    {
        var chatId = message.Id.ChatId;
        var telegramId = message.OwnerId;
        var messageId = message.Id.MessageId;

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeConfig.TryGetSome(out var filterConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(FilterConfig));
        }

        await messageAssistance.SendFilterRestrictMessage(chatId, telegramId, messageId, filterConfig,
            nameof(CheckBlackListCaptchaJob));

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, message.CaptchaMessageId, nameof(CheckBlackListCaptchaJob)),
            messageAssistance.DeleteCommandMessage(chatId, message.Id.MessageId, nameof(CheckBlackListCaptchaJob)),
            db.DeleteFilteredMessage(message.Id).UnitTask()).WhenAll();
    }
}
using DecembristChatBotSharp.Mongo;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class CheckMinionConfirmationJob(
    MinionRepository minionRepository,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var triggerKey = new TriggerKey(nameof(CheckMinionConfirmationJob));
        var job = JobBuilder.Create<CheckMinionConfirmationJob>()
            .WithIdentity(nameof(CheckMinionConfirmationJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithCronSchedule(appConfig.MinionConfig.ConfirmationCheckCron)
            .Build();
        
        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, trigger);
        }
        else
        {
            await scheduler.ScheduleJob(job, trigger);
        }
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Log.Information("Starting CheckMinionConfirmationJob - checking all minion confirmation messages");
        
        var allRelations = await minionRepository.GetAllMinionRelations();
        var revokedCount = 0;
        var checkedCount = 0;

        foreach (var relation in allRelations)
        {
            if (!relation.ConfirmationMessageId.HasValue)
            {
                Log.Warning("Minion {0} in chat {1} has no confirmation message ID", 
                    relation.Id.TelegramId, relation.Id.ChatId);
                continue;
            }

            checkedCount++;
            var messageExists = await CheckMessageExists(relation.Id.ChatId, relation.ConfirmationMessageId.Value);

            if (!messageExists)
            {
                // Message was deleted, revoke minion status
                Log.Information("Confirmation message {0} deleted for minion {1} in chat {2}, revoking status", 
                    relation.ConfirmationMessageId.Value, relation.Id.TelegramId, relation.Id.ChatId);

                var removed = await minionRepository.RemoveMinionRelation(relation.Id);
                if (removed)
                {
                    revokedCount++;
                    Log.Information("Revoked minion status for {0} in chat {1} - message deleted", 
                        relation.Id.TelegramId, relation.Id.ChatId);
                }
            }

            // Small delay to avoid rate limiting
            await Task.Delay(100, cancelToken.Token);
        }

        Log.Information("CheckMinionConfirmationJob completed - checked {0} minions, revoked {1}", 
            checkedCount, revokedCount);
    }

    private async Task<bool> CheckMessageExists(long chatId, int messageId)
    {
        const string reaction = "✍";
        ReactionTypeEmoji emoji = new() { Emoji = reaction };
        
        return await botClient.SetMessageReaction(chatId, messageId, [emoji], cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Map(_ => true)
            .IfFail(false);
    }
}

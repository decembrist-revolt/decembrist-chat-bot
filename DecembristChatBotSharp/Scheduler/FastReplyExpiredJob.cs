using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Quartz;
using Serilog;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Scheduler;

[Singleton]
public class FastReplyExpiredJob(
    AppConfig appConfig,
    MongoDatabase db,
    BotClient botClient,
    FastReplyRepository repository,
    CancellationTokenSource cancelToken) : IRegisterJob
{
    public async Task Register(IScheduler scheduler)
    {
        var job = JobBuilder.Create<FastReplyExpiredJob>()
            .WithIdentity(nameof(FastReplyExpiredJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(nameof(FastReplyExpiredJob))
            .StartNow()
            .WithCronSchedule(
                appConfig.CommandConfig.FastReplyCheckExpireCronUtc,
                x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Log.Information("Starting FastReplyExpiredJob");
        using var session = await db.OpenSession();
        session.StartTransaction();

        var replies = await repository.GetExpiredFastReplies(session);
        if (replies.IsEmpty)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Information("FastReplyExpiredJob finished with no expired replies");
            return;
        }

        if (!await repository.DeleteFastReplies(replies, session))
        {
            Log.Error("Failed to delete expired fast replies");
            await session.TryAbort(cancelToken.Token);
            return;
        }

        if (!await session.TryCommit(cancelToken.Token))
        {
            Log.Error("Failed to commit transaction for expired fast replies");
            return;
        }

        Log.Information("Marked {count} fast replies as expired", replies.Count);
        await replies.Map(SendReplyExpiredMessage).WhenAll();
    }

    private async Task SendReplyExpiredMessage(FastReply reply)
    {
        const string separator = FastReplyCommandHandler.ArgSeparator;
        Log.Information("Reply {0} expired in chat {1}", reply.Id.Message, reply.Id.ChatId);
        var replyText = reply.ReplyType switch
        {
            FastReplyType.Text => reply.Reply,
            FastReplyType.Sticker => FastReplyHandler.StickerPrefix + reply.Reply,
        };
        var fastReplyCommand =
            FastReplyCommandHandler.CommandKey + separator + reply.Id.Message.EscapeMarkdown() + separator + replyText.EscapeMarkdown();
        var username = await botClient.GetUsernameOrId(reply.TelegramId, reply.Id.ChatId, cancelToken.Token);
        var message = string.Format(appConfig.CommandConfig.FastReplyExpiredMessage, reply.Id.Message, username,
            fastReplyCommand);
        var chatId = reply.Id.ChatId;
        await botClient.SendMessageAndLog(chatId, message, parseMode: ParseMode.MarkdownV2,
            _ => Log.Information("Sent reply expired message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send reply expired message to chat {0}", chatId),
            cancelToken.Token);
    }
}
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class DislikeCommandHandler(
    DislikeRepository dislikeRepository,
    ExpiredMessageRepository expiredMessageRepository,
    MessageAssistance messageAssistance,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/dislike";
    public string Description => "Reply with this command to give the user a dislike";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var taskResult = parameters.ReplyToTelegramId.MatchAsync(
            None: async () => await SendReceiverNotSet(chatId),
            Some: async receiverId =>
            {
                if (receiverId == telegramId)
                    return await SendSelfMessage(chatId);

                var result =
                    await dislikeRepository.AddDislikeMember(new DislikeMember((telegramId, chatId), receiverId));
                return result switch
                {
                    DislikeResult.Exist => await SendExistDislike(chatId),
                    DislikeResult.Success => await SendSuccessMessage(chatId),
                    _ => unit
                };
            });
        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> SendExistDislike(long chatId)
    {
        var message = appConfig.DislikeConfig.ExistDislikeMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information("Sent dislike exist message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, "Failed to send dislike exist message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        var message = appConfig.DislikeConfig.ReceiverNotSetMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent dislike receiver not set message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send dislike receiver not set message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSelfMessage(long chatId)
    {
        var message = appConfig.DislikeConfig.SelfMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent self dislike message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send self dislike message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSuccessMessage(long chatId)
    {
        var message = appConfig.DislikeConfig.SuccessMessage;
        return await botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
            m =>
            {
                Log.Information("Sent dislike message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, "Failed to send dislike message to chat {0}", chatId),
            cancelToken.Token);
    }
}
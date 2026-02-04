using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
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
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/dislike";

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command,
            "Reply with this command to give the user a dislike");

    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        var maybeDislikeConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.DislikeConfig);
        if (!maybeDislikeConfig.TryGetSome(out var dislikeConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(DislikeConfig), Command);
        }

        var taskResult = parameters.ReplyToTelegramId.MatchAsync(
            None: async () => await SendReceiverNotSet(chatId, dislikeConfig),
            Some: async receiverId =>
            {
                if (receiverId == telegramId)
                    return await SendSelfMessage(chatId, dislikeConfig);

                var result =
                    await dislikeRepository.AddDislikeMember(new DislikeMember((telegramId, chatId), receiverId));
                return result switch
                {
                    DislikeResult.Exist => await SendExistDislike(chatId, dislikeConfig),
                    DislikeResult.Success => await SendSuccessMessage(chatId, dislikeConfig),
                    _ => unit
                };
            });
        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> SendExistDislike(long chatId, DislikeConfig dislikeConfig)
    {
        var message = dislikeConfig.ExistDislikeMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            m =>
            {
                Log.Information("Sent dislike exist message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId);
            },
            ex => Log.Error(ex, "Failed to send dislike exist message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId, DislikeConfig dislikeConfig)
    {
        var message = dislikeConfig.ReceiverNotSetMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent dislike receiver not set message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send dislike receiver not set message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSelfMessage(long chatId, DislikeConfig dislikeConfig)
    {
        var message = dislikeConfig.SelfMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent self dislike message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send self dislike message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSuccessMessage(long chatId, DislikeConfig dislikeConfig)
    {
        var message = dislikeConfig.SuccessMessage;
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
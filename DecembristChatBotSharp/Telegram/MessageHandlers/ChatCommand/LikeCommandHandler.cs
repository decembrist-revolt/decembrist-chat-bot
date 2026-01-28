using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Entity.Configs;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class LikeCommandHandler(
    AppConfig appConfig,
    CommandLockRepository lockRepository,
    MemberLikeRepository memberLikeRepository,
    BotClient botClient,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken
) : ICommandHandler
{
    public string Command => "/like";
    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command, "Reply with this command to give the user a like");
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var likeToTelegramId = parameters.ReplyToTelegramId;
        var messageId = parameters.MessageId;

        var maybeCommandConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.LikeConfig);
        if (!maybeCommandConfig.TryGetSome(out var likeConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(LikeConfig), Command);
        }

        if (likeToTelegramId.IsNone)
        {
            return await Array(
                SendLikeReceiverNotSet(chatId, likeConfig),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var receiverTelegramId = likeToTelegramId.ValueUnsafe();
        if (telegramId == receiverTelegramId)
        {
            return await Array(
                SendSelfLikeMessage(chatId, likeConfig),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
        }

        var lockSuccess = await lockRepository.TryAcquire(chatId, Command, telegramId: telegramId);
        if (!lockSuccess) return await messageAssistance.CommandNotReady(chatId, messageId, Command);

        if (!await SetLike(telegramId, chatId, receiverTelegramId)) return unit;

        var sendLikeMessageTask = botClient.GetChatMember(chatId, receiverTelegramId, cancelToken.Token)
            .ToTryAsync()
            .Bind(chatMember => SendLikeMessage(chatId, chatMember, likeConfig).ToTryAsync())
            .Match(
                _ => Log.Information("Sent like message to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send like message to chat {0}", chatId)
            );

        return await Array(
            sendLikeMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<bool> SetLike(long telegramId, long chatId, long receiverTelegramId)
    {
        var likes = await memberLikeRepository.FindMemberLikes(telegramId, chatId);
        if (likes.Any())
        {
            var like = likes.First();
            if (!await memberLikeRepository.RemoveMemberLike(telegramId, chatId, like.LikeTelegramId))
            {
                return false;
            }
        }

        await memberLikeRepository.AddMemberLike(telegramId, chatId, receiverTelegramId);

        Log.Information("Member {0} liked {1} in chat {2}", telegramId, receiverTelegramId, chatId);

        return true;
    }

    private async Task<Unit> SendLikeReceiverNotSet(long chatId, LikeConfig likeConfig)
    {
        var message = likeConfig.LikeReceiverNotSet;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent like receiver not set message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send like receiver not set message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendSelfLikeMessage(long chatId, LikeConfig likeConfig)
    {
        var message = likeConfig.SelfLikeMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent self like message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send self like message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendLikeMessage(long chatId, ChatMember chatMember, LikeConfig likeConfig)
    {
        var username = chatMember.GetUsername();
        var message = string.Format(likeConfig.LikeMessage, username);
        await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);

        return unit;
    }
}
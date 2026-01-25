using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class MinionCommandHandler(
    BotClient botClient,
    AppConfig appConfig,
    PremiumMemberService premiumMemberService,
    MinionRepository minionRepository,
    MinionInvitationRepository minionInvitationRepository,
    MessageAssistance messageAssistance,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/minion";
    public string Command => CommandKey;

    public string Description =>
        appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
            "Make a chat member your minion (premium only)");

    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload) return unit;

        var taskResult = parameters.ReplyToTelegramId.MatchAsync(
            async receiverId => await HandleMinion(receiverId, chatId, telegramId, parameters.ReplyToMessageId),
            async () => await SendReceiverNotSet(chatId));

        return await Array(
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command),
            taskResult).WhenAll();
    }

    private async Task<Unit> HandleMinion(long receiverId, long chatId, long telegramId, Option<int> replyToMessageId)
    {
        // Check if sender is premium
        var isPremium = await premiumMemberService.IsPremium(telegramId, chatId);
        if (!isPremium) return await SendNotPremiumMessage(chatId);

        // Check if receiver is the sender
        if (receiverId == telegramId) return unit; // Silent fail for self-targeting

        // Check if sender already has a pending invitation (not a minion yet!)
        var existingInvitation = await minionInvitationRepository.GetInvitationByMaster(telegramId, chatId);
        if (existingInvitation.IsSome)
        {
            var invitation =
                existingInvitation.IfNone(() => new MinionInvitation((0, chatId), telegramId, 0, DateTime.UtcNow));
            var invitedUsername = await botClient.GetUsername(chatId, invitation.Id.TelegramId, cancelToken.Token)
                .ToAsync()
                .IfNone(invitation.Id.TelegramId.ToString);
            return await SendAlreadyHasInvitationMessage(chatId, invitedUsername);
        }

        // Check if sender already has a minion
        var existingMinion = await minionRepository.GetMinionByMaster(telegramId, chatId);
        if (existingMinion.IsSome)
        {
            return await existingMinion
                .Filter(minion => minion.Id.TelegramId == receiverId)
                .MatchAsync(
                    minion => ShowMinionStatus(chatId, telegramId, receiverId, minion.ConfirmationMessageId),
                    () => SendAlreadyHasMinionMessage(chatId));
        }

        // Check if receiver is already a minion of someone else
        var existingMinionRelation = await minionRepository.GetMinionRelation((receiverId, chatId));
        if (existingMinionRelation.IsSome)
        {
            // Already minion of someone else
            var receiverUsername = await botClient.GetUsername(chatId, receiverId, cancelToken.Token)
                .ToAsync()
                .IfNone(receiverId.ToString);
            return await SendAlreadyIsMinionMessage(chatId, receiverUsername);
        }

        // Check if receiver is premium
        var isReceiverPremium = await premiumMemberService.IsPremium(receiverId, chatId);
        if (isReceiverPremium)
        {
            return await SendTargetIsPremiumMessage(chatId);
        }


        // Send invitation message
        return await SendInvitationMessage(chatId, telegramId, receiverId, replyToMessageId);
    }

    private async Task<Unit> SendInvitationMessage(long chatId, long masterTelegramId, long minionTelegramId,
        Option<int> replyToMessageId)
    {
        var masterUsername = await botClient.GetUsername(chatId, masterTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterTelegramId.ToString);

        var expirationMinutes = appConfig.MinionConfig.InvitationExpirationMinutes;
        var message = string.Format(appConfig.MinionConfig.InvitationMessage, masterUsername, expirationMinutes);
        const string logTemplate = "Minion invitation message sent {0} ChatId: {1}, Master: {2}, Target: {3}";

        // Send message as reply if replyToMessageId is provided
        return await replyToMessageId.MatchAsync(
            async replyId => await botClient.SendMessageAndLog(chatId, message, replyId,
                async m =>
                {
                    Log.Information(logTemplate, "success", chatId, masterTelegramId, minionTelegramId);

                    // Create invitation in database with TTL
                    var invitation = new MinionInvitation(
                        (minionTelegramId, chatId),
                        masterTelegramId,
                        m.MessageId,
                        DateTime.UtcNow,
                        DateTime.UtcNow.AddMinutes(expirationMinutes)
                    );

                    await minionInvitationRepository.AddInvitation(invitation);

                    // Queue message for auto-deletion
                    expiredMessageRepository.QueueMessage(chatId, m.MessageId,
                        DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes));
                },
                ex => Log.Error(ex, logTemplate, "failed", chatId, masterTelegramId, minionTelegramId),
                cancelToken.Token),
            async () => await botClient.SendMessageAndLog(chatId, message,
                async m =>
                {
                    Log.Information(logTemplate, "success", chatId, masterTelegramId, minionTelegramId);

                    // Create invitation in database with TTL
                    var invitation = new MinionInvitation(
                        (minionTelegramId, chatId),
                        masterTelegramId,
                        m.MessageId,
                        DateTime.UtcNow,
                        DateTime.UtcNow.AddMinutes(expirationMinutes)
                    );

                    await minionInvitationRepository.AddInvitation(invitation);

                    // Queue message for auto-deletion
                    expiredMessageRepository.QueueMessage(chatId, m.MessageId,
                        DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes));
                },
                ex => Log.Error(ex, logTemplate, "failed", chatId, masterTelegramId, minionTelegramId),
                cancelToken.Token)
        );
    }

    private async Task<Unit> ShowMinionStatus(long chatId, long masterTelegramId, long minionTelegramId,
        int? confirmationMessageId)
    {
        var masterUsername = await botClient.GetUsername(chatId, masterTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterTelegramId.ToString);
        var minionUsername = await botClient.GetUsername(chatId, minionTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionTelegramId.ToString);

        // Escape special characters for MarkdownV2
        var escapedMinionUsername = EscapeMarkdownV2(minionUsername);
        var escapedMasterUsername = EscapeMarkdownV2(masterUsername);

        var message = string.Format(appConfig.MinionConfig.ShowMinionStatusMessage, escapedMinionUsername, escapedMasterUsername);

        // If we have confirmation message ID, add a link to it
        if (confirmationMessageId.HasValue)
        {
            var chatIdStr = chatId.ToString().Replace("-100", "");
            message += $"\n\n[Сообщение подтверждения](https://t.me/c/{chatIdStr}/{confirmationMessageId})";
        }

        const string logTemplate = "Minion status shown {0} ChatId: {1}, Master: {2}, Minion: {3}";
        return await botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
            m =>
            {
                Log.Information(logTemplate, "success", chatId, masterTelegramId, minionTelegramId);
                var expirationDate = DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes);
                expiredMessageRepository.QueueMessage(chatId, m.MessageId, expirationDate);
            },
            ex => Log.Error(ex, logTemplate, "failed", chatId, masterTelegramId, minionTelegramId),
            cancelToken.Token);
    }

    private static string EscapeMarkdownV2(string text)
    {
        // Escape special characters for MarkdownV2: _*[]()~`>#+-=|{}.!
        var specialChars = new[] { '_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        foreach (var ch in specialChars)
        {
            text = text.Replace(ch.ToString(), $"\\{ch}");
        }
        return text;
    }

    private async Task<Unit> SendNotPremiumMessage(long chatId)
    {
        const string logTemplate = "Not premium message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, appConfig.MinionConfig.NotPremiumMessage,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendTargetIsPremiumMessage(long chatId)
    {
        const string logTemplate = "Target is premium message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, appConfig.MinionConfig.TargetIsPremiumMessage,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendAlreadyHasMinionMessage(long chatId)
    {
        const string logTemplate = "Already has minion message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, appConfig.MinionConfig.AlreadyHasMinionMessage,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendAlreadyHasInvitationMessage(long chatId, string invitedUsername)
    {
        var expirationMinutes = appConfig.MinionConfig.InvitationExpirationMinutes;
        var message = string.Format(appConfig.MinionConfig.AlreadyHasInvitationMessage, invitedUsername,
            expirationMinutes);
        const string logTemplate = "Already has invitation message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendAlreadyIsMinionMessage(long chatId, string receiverUsername)
    {
        var message = string.Format(appConfig.MinionConfig.AlreadyIsMinionMessage, receiverUsername);
        const string logTemplate = "Already is minion message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId)
    {
        const string logTemplate = "Receiver not set message sent {0} ChatId: {1}";
        return await botClient.SendMessageAndLog(chatId, appConfig.MinionConfig.ReceiverNotSetMessage,
            _ => Log.Information(logTemplate, "success", chatId),
            ex => Log.Error(ex, logTemplate, "failed", chatId),
            cancelToken.Token);
    }
}
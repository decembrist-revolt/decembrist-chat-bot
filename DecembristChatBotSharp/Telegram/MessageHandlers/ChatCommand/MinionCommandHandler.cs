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
    CancellationTokenSource cancelToken,
    MinionService minionService) : ICommandHandler
{
    public const string CommandKey = "/minion";
    public string Command => CommandKey;

    public string Description => appConfig.CommandConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
        "Make a chat member your minion (premium only)");

    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload) return unit;
        await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

        var isPremium = await premiumMemberService.IsPremium(telegramId, chatId);
        if (!isPremium) return await SendNotPremiumMessage(chatId);

        return await parameters.ReplyToTelegramId.Bind(receiverId =>
                parameters.ReplyToMessageId.Map(replyMessageId =>
                    HandleMinion(receiverId, chatId, telegramId, replyMessageId)))
            .IfNone(() => SendReceiverNotSet(chatId));
    }

    private async Task<Unit> HandleMinion(long receiverId, long chatId, long telegramId, int replyToMessageId)
    {
        // Check if receiver is the sender
        if (receiverId == telegramId) return unit; // Silent fail for self-targeting

        // Check if sender already has a pending invitation (not a minion yet!)
        var existingInvitation = await minionInvitationRepository.GetInvitationByMaster(telegramId, chatId);
        if (existingInvitation.TryGetSome(out var invitation))
        {
            var invitedUsername = await botClient.GetUsernameOrId(invitation.Id.TelegramId, chatId, cancelToken.Token);
            return await SendAlreadyHasInvitationMessage(chatId, invitedUsername);
        }

        // Check if sender already has a minion
        var existingMinion = await minionRepository.GetMinionByMaster(telegramId, chatId);
        if (existingMinion.IsSome)
        {
            return await existingMinion
                .Filter(minion => minion.Id.TelegramId == receiverId)
                .MatchAsync(minion => ShowMinionStatus(chatId, telegramId, receiverId, minion.ConfirmationMessageId),
                    () => SendAlreadyHasMinionMessage(chatId));
        }

        // Check if receiver is already a minion of someone else
        var existingMinionRelation = await minionRepository.GetMinionRelation((receiverId, chatId));
        if (existingMinionRelation.IsSome)
        {
            var receiverUsername = await botClient.GetUsernameOrId(receiverId, chatId, cancelToken.Token);
            return await SendAlreadyIsMinionMessage(chatId, receiverUsername);
        }

        // Check if receiver is premium
        var isReceiverPremium = await premiumMemberService.IsPremium(receiverId, chatId);
        if (isReceiverPremium)
        {
            return await SendTargetIsPremiumMessage(chatId);
        }

        return await SendInvitationMessage(chatId, telegramId, receiverId, replyToMessageId);
    }

    private async Task<Unit> SendInvitationMessage(long chatId, long masterId, long minionId, int replyToMessageId)
    {
        var masterName = await botClient.GetUsernameOrId(masterId, chatId, cancelToken.Token);

        var expirationMinutes = appConfig.MinionConfig.InvitationExpirationMinutes;
        var message = string.Format(appConfig.MinionConfig.InvitationMessage, masterName, expirationMinutes);
        const string logTemplate = "Minion invitation message sent {0} ChatId: {1}, Master: {2}, Target: {3}";

        return await botClient.SendMessageAndLog(chatId, message, replyToMessageId, async m =>
            {
                Log.Information(logTemplate, "success", chatId, masterId, minionId);

                var invitation = new MinionInvitation((minionId, chatId),
                    masterId,
                    m.MessageId,
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddMinutes(expirationMinutes)
                );

                expiredMessageRepository.QueueMessage(chatId, m.MessageId,
                    DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes));
                await minionInvitationRepository.AddInvitation(invitation);
            },
            ex => Log.Error(ex, logTemplate, "failed", chatId, masterId, minionId),
            cancelToken.Token);
    }

    private async Task<Unit> ShowMinionStatus(long chatId, long masterId, long minionId,
        int? confirmationMessageId)
    {
        var (masterName, minionName) = await minionService.GetMasterMinionNames(chatId, masterId, minionId);

        var message = string.Format(appConfig.MinionConfig.ShowMinionStatusMessage, minionName.EscapeMarkdown(),
            masterName.EscapeMarkdown());

        // If we have confirmation message ID, add a link to it
        if (confirmationMessageId.HasValue)
        {
            var chatIdStr = chatId.ToString().Replace("-100", "");
            message += $"\n\n[Сообщение подтверждения](https://t.me/c/{chatIdStr}/{confirmationMessageId})";
        }

        Log.Information("Minion status shown success ChatId: {chatId}, Master: {masterId}, Minion: {minionId}",
            chatId, masterId, minionId);
        var expirationDate = DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes);
        return await messageAssistance.SendCommandResponse(chatId, message, Command, expirationDate,
            parseMode: ParseMode.Markdown);
    }

    private Task<Unit> SendNotPremiumMessage(long chatId)
    {
        Log.Information("Not premium message sent ChatId: {chatId}", chatId);
        return messageAssistance.SendCommandResponse(chatId, appConfig.MinionConfig.NotPremiumMessage, Command);
    }

    private Task<Unit> SendTargetIsPremiumMessage(long chatId)
    {
        Log.Information("Target is premium message sent ChatId: {0}", chatId);
        return messageAssistance.SendCommandResponse(chatId, appConfig.MinionConfig.TargetIsPremiumMessage, Command);
    }

    private async Task<Unit> SendAlreadyHasMinionMessage(long chatId)
    {
        Log.Information("Target is premium message sent ChatId: {0}", chatId);
        return await messageAssistance.SendCommandResponse(chatId, appConfig.MinionConfig.AlreadyHasMinionMessage,
            Command);
    }

    private async Task<Unit> SendAlreadyHasInvitationMessage(long chatId, string invitedUsername)
    {
        var expirationMinutes = appConfig.MinionConfig.InvitationExpirationMinutes;
        var message = string.Format(appConfig.MinionConfig.AlreadyHasInvitationMessage, invitedUsername,
            expirationMinutes);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendAlreadyIsMinionMessage(long chatId, string receiverUsername)
    {
        var message = string.Format(appConfig.MinionConfig.AlreadyIsMinionMessage, receiverUsername);
        return await messageAssistance.SendCommandResponse(chatId, message, Command);
    }

    private async Task<Unit> SendReceiverNotSet(long chatId) =>
        await messageAssistance.SendCommandResponse(chatId, appConfig.MinionConfig.ReceiverNotSetMessage, Command);
}
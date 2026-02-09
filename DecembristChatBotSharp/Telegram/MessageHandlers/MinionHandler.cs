using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class MinionHandler(
    MinionRepository minionRepository,
    MinionInvitationRepository minionInvitationRepository,
    PremiumMemberRepository premiumMemberRepository,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    MinionService minionService,
    MessageAssistance messageAssistance)
{
    /// <summary>
    /// Handles when user writes confirmation message "Я хочу служить {Name}"
    /// </summary>
    public async Task<bool> HandleMinionConfirmation(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return false;

        // Check if message matches pattern "Я хочу служить {Name}"
        if (!text.StartsWith(appConfig.MinionConfig.MinionConfirmationPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        // Extract master name from message
        var masterName = text[appConfig.MinionConfig.MinionConfirmationPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(masterName)) return false;

        // Check if there's a pending invitation for this user
        var invitation = await minionInvitationRepository.GetInvitation((telegramId, chatId));
        if (!invitation.TryGetSome(out var inv))
        {
            Log.Information("No pending invitation found for user {0} in chat {1}", telegramId, chatId);
            return false;
        }

        // Verify that the master name matches
        var masterUsername = await botClient.GetUsernameOrId(inv.MasterTelegramId, chatId, cancelToken.Token);

        if (!masterUsername.Equals(masterName, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Master name mismatch for minion {0} in chat {1}: expected {2}, got {3}",
                telegramId, chatId, masterUsername, masterName);
            return false;
        }

        // Check if user is still not premium (they might have become premium after invitation)
        var isSenderPremium = await premiumMemberRepository.IsPremium((telegramId, chatId));
        if (isSenderPremium)
        {
            Log.Information("User {0} became premium, cannot accept minion invitation in chat {1}", telegramId, chatId);
            await minionInvitationRepository.RemoveInvitation((telegramId, chatId));
            return false;
        }

        // Check if master is still premium
        var isMasterPremium = await premiumMemberRepository.IsPremium((inv.MasterTelegramId, chatId));
        if (!isMasterPremium)
        {
            Log.Information("Master {0} is no longer premium in chat {1}", inv.MasterTelegramId, chatId);
            await minionInvitationRepository.RemoveInvitation((telegramId, chatId));
            return false;
        }

        // Create the actual minion relationship
        var relation = new MinionRelation(
            (telegramId, chatId),
            inv.MasterTelegramId,
            messageId,
            DateTime.UtcNow
        );

        var added = await minionRepository.AddMinionRelation(relation);
        if (added)
        {
            // Remove the invitation
            await minionInvitationRepository.RemoveInvitation((telegramId, chatId));

            // Delete the invitation message
            Log.Information("Deleted invitation message {0} in chat {1}", inv.InvitationMessageId, chatId);
            await messageAssistance.DeleteCommandMessage(chatId, inv.InvitationMessageId, nameof(MinionHandler));

            // Send notification to chat that minion relationship was created
            await SendMinionCreatedMessage(chatId, telegramId, inv.MasterTelegramId);

            Log.Information("Minion relationship created and invitation removed: {0} -> {1} in chat {2}",
                telegramId, inv.MasterTelegramId, chatId);
            return true;
        }

        Log.Error("Failed to create minion relationship: {0} -> {1} in chat {2}",
            telegramId, inv.MasterTelegramId, chatId);
        return false;
    }

    private async Task<Unit> SendMinionCreatedMessage(long chatId, long minionId, long masterId)
    {
        var (minionName, masterName) = await minionService.GetMasterMinionNames(chatId, masterId, minionId);

        var message = string.Format(appConfig.MinionConfig.MinionCreatedMessage, minionName, masterName);
        var expirationMinutes = DateTime.UtcNow.AddMinutes(appConfig.MinionConfig.MessageExpirationMinutes);
        Log.Information("Sending minion created message to chat {0}: {1} -> {2}", chatId, minionId, masterId);
        return await messageAssistance.SendMessageExpired(chatId, message, nameof(MinionHandler), expirationMinutes);
    }

    /// <summary>
    /// Handles when confirmation message is deleted - revokes minion status
    /// </summary>
    public async Task<bool> HandleMessageDeletion(long chatId, long telegramId, int messageId)
    {
        // Check if this message is a confirmation message for a minion relationship
        var minionRelation = await minionRepository.GetMinionRelation((telegramId, chatId));

        return await minionRelation
            .Filter(relation => relation.ConfirmationMessageId == messageId)
            .MatchAsync(
                Some: async relation =>
                {
                    var removed = await minionRepository.RemoveMinionRelation((telegramId, chatId));
                    if (removed)
                    {
                        Log.Information("Minion relationship revoked by message deletion: {0} in chat {1}",
                            telegramId, chatId);
                    }

                    return removed;
                },
                None: () => Task.FromResult(false)
            );
    }

    /// <summary>
    /// Checks if minion's confirmation message still exists. If not, revokes minion status.
    /// Called when minion sends a new message in chat.
    /// </summary>
    public async Task<bool> CheckConfirmationMessageExists(long chatId, long telegramId)
    {
        var minionRelation = await minionRepository.GetMinionRelation((telegramId, chatId));
        if (minionRelation.IsNone) return false;

        var relation = minionRelation.IfNone(() => new MinionRelation((telegramId, chatId), 0, null, null));
        if (!relation.ConfirmationMessageId.HasValue) return false;

        // Try to set reaction on confirmation message to check if it exists
        var messageExists = await TrySetReaction(chatId, relation.ConfirmationMessageId.Value);

        if (!messageExists)
        {
            // Message was deleted, revoke minion status
            Log.Information("Confirmation message {0} deleted for minion {1} in chat {2}, revoking status",
                relation.ConfirmationMessageId.Value, telegramId, chatId);

            await minionRepository.RemoveMinionRelation((telegramId, chatId));
            return true; // Status was revoked
        }

        return false; // Message still exists, status intact
    }

    private async Task<bool> TrySetReaction(long chatId, int messageId)
    {
        const string reaction = "👁";
        ReactionTypeEmoji emoji = new() { Emoji = reaction };
        return await botClient.SetMessageReaction(chatId, messageId, [emoji], cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Map(_ => true)
            .IfFail(false);
    }
}
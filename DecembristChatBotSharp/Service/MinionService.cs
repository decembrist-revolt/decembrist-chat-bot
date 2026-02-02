using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MinionService(
    MinionRepository minionRepository,
    MessageAssistance messageAssistance,
    MinionInvitationRepository minionInvitationRepository,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    /// <summary>
    /// Checks if a user has a minion and returns the minion's ID
    /// </summary>
    public async Task<Option<long>> GetMinionId(long masterTelegramId, long chatId)
    {
        var minionRelation = await minionRepository.GetMinionByMaster(masterTelegramId, chatId);
        return minionRelation.Map(relation => relation.Id.TelegramId);
    }

    /// <summary>
    /// Checks if a user is a minion and returns the master's ID
    /// </summary>
    public async Task<Option<long>> GetMasterId(long minionTelegramId, long chatId)
    {
        var minionRelation = await minionRepository.GetMinionRelation((minionTelegramId, chatId));
        return minionRelation.Map(relation => relation.MasterTelegramId);
    }

    public async Task<(string, string)> GetMasterMinionNames(long chatId, long masterId, long minionId)
    {
        var masterUsername = await botClient.GetUsernameOrId(masterId, chatId, cancelToken.Token);
        var minionUsername = await botClient.GetUsernameOrId(minionId, chatId, cancelToken.Token);
        return (masterUsername, minionUsername);
    }

    /// <summary>
    /// Revokes minion status when the minion becomes premium
    /// </summary>
    public async Task RevokeMinionStatusOnBecomingPremium(long telegramId, long chatId)
    {
        var minionRelation = await minionRepository.GetMinionRelation((telegramId, chatId));
        if (minionRelation.IsSome)
        {
            var removed = await minionRepository.RemoveMinionRelation((telegramId, chatId));
            if (removed)
            {
                await SendMinionRevokedByBecomingPremiumMessage(chatId, telegramId);
                Log.Information("Revoked minion status for {0} in chat {1} - became premium", telegramId, chatId);
            }
        }

        // Also remove pending invitation if exists
        var invitationRemoved = await minionInvitationRepository.RemoveInvitation((telegramId, chatId));
        if (invitationRemoved)
        {
            Log.Information("Removed pending minion invitation for {0} in chat {1} - became premium",
                telegramId, chatId);
        }
    }

    /// <summary>
    /// Revokes all minion relationships when a master loses premium status
    /// </summary>
    public async Task RevokeMinionStatusOnPremiumLoss(long masterTelegramId, long chatId)
    {
        // Remove actual minion relationship
        var minionRelation = await minionRepository.GetMinionByMaster(masterTelegramId, chatId);
        if (minionRelation.TryGetSome(out var relation))
        {
            var removed = await minionRepository.RemoveMinionByMaster(masterTelegramId, chatId);
            if (removed)
            {
                await SendMinionRevokedByPremiumLossMessage(chatId, relation.Id.TelegramId, masterTelegramId);
                Log.Information("Revoked minion {0} for master {1} in chat {2} - lost premium",
                    relation.Id.TelegramId, masterTelegramId, chatId);
            }
        }

        // Also remove pending invitation if exists
        var invitationRemoved = await minionInvitationRepository.RemoveInvitationByMaster(masterTelegramId, chatId);
        if (invitationRemoved)
        {
            Log.Information("Removed pending minion invitation for master {0} in chat {1} - lost premium",
                masterTelegramId, chatId);
        }
    }

    private async Task SendMinionRevokedByPremiumLossMessage(long chatId, long minionId, long masterId)
    {
        var (masterUsername, minionUsername) = await GetMasterMinionNames(chatId, masterId, minionId);
        var message = string.Format(appConfig.MinionConfig.MinionRevokedByPremiumLossMessage, minionUsername,
            masterUsername);
        await messageAssistance.SendCommandResponse(chatId, message, nameof(MinionService));
    }

    private async Task SendMinionRevokedByBecomingPremiumMessage(long chatId, long minionId)
    {
        var minionUsername = await botClient.GetUsernameOrId(minionId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.MinionConfig.MinionRevokedByBecomingPremiumMessage, minionUsername);
        await messageAssistance.SendCommandResponse(chatId, message, nameof(MinionService));
    }

    public async Task SendMinionRevokedByDeleteMessage(long chatId, long minionId)
    {
        var minionUsername = await botClient.GetUsernameOrId(minionId, chatId, cancelToken.Token);
        var message = string.Format(appConfig.MinionConfig.MinionRevokedByDeleteMessage, minionUsername);
        await messageAssistance.SendCommandResponse(chatId, message, nameof(MinionService));
    }

    public async Task<Option<long>> GetRedirectTarget(long targetTelegramId, long chatId)
    {
        // Check if target has a minion - if so, redirect to the minion
        var minionIdOpt = await GetMinionId(targetTelegramId, chatId);
        return minionIdOpt;
    }

    public async Task<Unit> SendNegativeEffectRedirectMessage(long chatId, long masterTelegramId, long minionTelegramId)
    {
        var (masterName, minionName) = await GetMasterMinionNames(chatId, masterTelegramId, minionTelegramId);
        var message = string.Format(appConfig.MinionConfig.NegativeEffectRedirectMessage, masterName, minionName);
        return await messageAssistance.SendCommandResponse(chatId, message, nameof(MinionService));
    }
}
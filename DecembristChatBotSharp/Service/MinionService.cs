using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MinionService(
    MinionRepository minionRepository,
    MinionInvitationRepository minionInvitationRepository,
    MemberItemRepository memberItemRepository,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    MemberItemService memberItemService)
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

    /// <summary>
    /// Transfers an amulet from a minion to their master
    /// </summary>
    public async Task<bool> TransferAmuletToMaster(long minionTelegramId, long masterId, long chatId,
        IMongoSession session)
    {
        var success = await memberItemService.HandleAmuletItem((masterId, chatId), session);
        if (!success)
        {
            var added = await memberItemRepository.AddMemberItem(chatId, masterId, MemberItemType.Amulet, session);
            if (!added) return false;
        }

        await SendAmuletTransferMessage(chatId, minionTelegramId, masterId);

        Log.Information("Transferred amulet from minion {minionId} to master {masterId} in chat {chatId}",
            minionTelegramId, masterId, chatId);
        return true;
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
        if (minionRelation.IsSome)
        {
            var removed = await minionRepository.RemoveMinionByMaster(masterTelegramId, chatId);
            if (removed)
            {
                var relation =
                    minionRelation.IfNone(() => new MinionRelation((0, chatId), masterTelegramId, null, null));
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

    private async Task SendAmuletTransferMessage(long chatId, long minionId, long masterId)
    {
        var minionUsername = await botClient.GetUsername(chatId, minionId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionId.ToString);
        var masterUsername = await botClient.GetUsername(chatId, masterId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterId.ToString);

        var message = string.Format(appConfig.MinionConfig.AmuletTransferMessage, minionUsername, masterUsername);
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent amulet transfer message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send amulet transfer message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task SendStoneTransferMessage(long chatId, long masterId, long minionId)
    {
        var masterUsername = await botClient.GetUsername(chatId, masterId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterId.ToString);
        var minionUsername = await botClient.GetUsername(chatId, minionId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionId.ToString);

        var message = string.Format(appConfig.MinionConfig.StoneTransferMessage, masterUsername, minionUsername);
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent stone transfer message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send stone transfer message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task SendMinionRevokedByPremiumLossMessage(long chatId, long minionId, long masterId)
    {
        var minionUsername = await botClient.GetUsername(chatId, minionId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionId.ToString);
        var masterUsername = await botClient.GetUsername(chatId, masterId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterId.ToString);

        var message = string.Format(appConfig.MinionConfig.MinionRevokedByPremiumLossMessage, minionUsername,
            masterUsername);
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent minion revoked by premium loss message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send minion revoked message to chat {0}", chatId),
            cancelToken.Token);
    }

    private async Task SendMinionRevokedByBecomingPremiumMessage(long chatId, long minionId)
    {
        var minionUsername = await botClient.GetUsername(chatId, minionId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionId.ToString);

        var message = string.Format(appConfig.MinionConfig.MinionRevokedByBecomingPremiumMessage, minionUsername);
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent minion revoked by becoming premium message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send minion revoked message to chat {0}", chatId),
            cancelToken.Token);
    }

    public async Task<Option<long>> GetRedirectTarget(long targetTelegramId, long chatId)
    {
        // Check if target has a minion - if so, redirect to the minion
        var minionIdOpt = await GetMinionId(targetTelegramId, chatId);
        return minionIdOpt;
    }

    public async Task SendNegativeEffectRedirectMessage(long chatId, long masterTelegramId, long minionTelegramId)
    {
        var masterUsername = await botClient.GetUsername(chatId, masterTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(masterTelegramId.ToString);
        var minionUsername = await botClient.GetUsername(chatId, minionTelegramId, cancelToken.Token)
            .ToAsync()
            .IfNone(minionTelegramId.ToString);

        var message = string.Format(appConfig.MinionConfig.NegativeEffectRedirectMessage, masterUsername,
            minionUsername);
        await botClient.SendMessageAndLog(chatId, message,
            _ => Log.Information("Sent negative effect redirect message to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send negative effect redirect message to chat {0}", chatId),
            cancelToken.Token);
    }
}
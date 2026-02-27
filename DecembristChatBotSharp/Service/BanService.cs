using System.Runtime.CompilerServices;
using Lamar;
using Serilog;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class BanService(BotClient botClient, CancellationTokenSource cancelToken)
{
    public Task<Unit> BanChatMember(
        long chatId,
        long telegramId,
        bool isRevokeMessages = false,
        [CallerMemberName] string callerName = "UnknownCaller") =>
        botClient.BanChatMemberAndLog(chatId, telegramId,
            _ =>
                Log.Information("Ban chat member: from: {0}, userId: {1}, chat:{2}",
                    callerName, telegramId, chatId),
            ex =>
                Log.Error(ex, "Failed to ban chat member: from {0}, userId:{1} to chat {2}",
                    callerName, telegramId, chatId),
            cancelToken.Token, isRevokeMessages);

    public Task<Unit> UnbanChatMember(
        long chatId, long telegramId, [CallerMemberName] string callerName = "UnknownCaller") =>
        botClient.UnbanChatMemberAndLog(chatId, telegramId,
            _ =>
                Log.Information("Unban chat member: from: {0}, userId: {1}, chat:{2}",
                    callerName, telegramId, chatId),
            ex =>
                Log.Error(ex, "Failed to unban chat member: from {0}, userId:{1} to chat {2}",
                    callerName, telegramId, chatId),
            cancelToken.Token);

    public async Task<Unit> KickChatMember(
        long chatId, long telegramId, [CallerMemberName] string callerName = "UnknownCaller")
    {
        await BanChatMember(chatId, telegramId, callerName: callerName);
        return await UnbanChatMember(chatId, telegramId, callerName);
    }

    public Task<Unit> RestrictChatMember(long chatId, long telegramId)
    {
        var permissions = new ChatPermissions
        {
            CanSendMessages = false,
            CanSendAudios = false,
            CanSendDocuments = false,
            CanSendPhotos = false,
            CanSendVideos = false,
            CanSendVideoNotes = false,
            CanSendVoiceNotes = false,
            CanSendOtherMessages = false,
            CanAddWebPagePreviews = false,
        };
        return botClient.RestrictUserAndLog(
            chatId,
            telegramId,
            permissions,
            _ => Log.Information("Restriction for user {0} in chat {1}", telegramId, chatId),
            ex => Log.Error(ex, "Failed to apply timeout restriction for user {0} in chat {1}",
                telegramId, chatId), cancellationToken: cancelToken.Token);
    }

    public async Task<Unit> UnRestrictChatMember(long chatId, long telegramId)
    {
        var permissions = new ChatPermissions
        {
            CanSendMessages = true,
            CanSendAudios = true,
            CanSendDocuments = true,
            CanSendPhotos = true,
            CanSendVideos = true,
            CanSendVideoNotes = true,
            CanSendVoiceNotes = true,
            CanSendOtherMessages = true,
            CanAddWebPagePreviews = true,
            CanInviteUsers = true,
        };

        return await botClient.RestrictUserAndLog(
            chatId,
            telegramId,
            permissions,
            _ => Log.Information("UnRestriction for user {0} in chat {1}", telegramId, chatId),
            ex => Log.Error(ex, "Failed to apply timeout unRestriction for user {0} in chat {1}",
                telegramId, chatId),
            cancellationToken: cancelToken.Token);
    }
}
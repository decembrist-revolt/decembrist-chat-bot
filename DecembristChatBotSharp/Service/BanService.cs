using System.Runtime.CompilerServices;
using Lamar;
using Serilog;
using Telegram.Bot;
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

    public async Task<Unit> RestrictChatMember(long chatId, long telegramId)
    {
        var permissions = new ChatPermissions
        {
            CanSendMessages = false,
        };

        await botClient.RestrictChatMember(
                chatId: chatId,
                userId: telegramId,
                permissions: permissions,
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("Restriction for user {0} in chat {1}", telegramId, chatId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to apply timeout restriction for user {0} in chat {1}",
                        telegramId, chatId);
                    return false;
                });
        return unit;
    }

    public async Task<Unit> UnRestrictChatMember(long chatId, long telegramId) =>
        await botClient.RestrictChatMember(
                chatId: chatId,
                userId: telegramId,
                permissions: new ChatPermissions { CanSendMessages = true },
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ =>
                {
                    Log.Information("UnRestriction for user {0} in chat {1}", telegramId, chatId);
                    return unit;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to apply timeout un restriction for user {0} in chat {1}",
                        telegramId, chatId);
                    return unit;
                });
}
using System.Runtime.CompilerServices;
using Lamar;
using Serilog;

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
}
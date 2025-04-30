using System.Runtime.CompilerServices;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram;

public static class LogAssistant
{
    public static Unit LogDeleteResult(
        bool isDelete,
        long receiverId,
        long chatId,
        long adminId,
        string commandName,
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        if (isDelete)
        {
            Log.Information("Delete success: '{0}' from: {1}, removed: {2}, chat: {3}, by: {4}",
                commandName, callerName, receiverId, chatId, adminId);
        }
        else
        {
            Log.Error("Delete failed: '{0}' from: {1}, removed: {2}, chat: {3}, by: {4}",
                commandName, callerName, receiverId, chatId, adminId);
        }

        return unit;
    }

    public static T LogSuccessUsingItem<T>(this T maybeResult,
        long chatId,
        long telegramId,
        [CallerMemberName] string callerName = "unknownCaller") where T : Enum
    {
        Log.Information("Item usage succeeded from: {0}, User: {1}, Chat: {2}, Result: {3}",
            callerName, telegramId, chatId, maybeResult);
        return maybeResult;
    }
}
using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram;

[Singleton]
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

    public static void LogDustResult(this DustOperationResult dustResult,
        long telegramId,
        long chatId,
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        var result = dustResult.Result;
        switch (result)
        {
            case DustResult.Success or DustResult.PremiumSuccess:
                Log.Information("Dust operation SUCCESS: {0} from: {1}, chat: {2}, by: {3}",
                    callerName, result, chatId, telegramId);
                break;
            case DustResult.Failed:
                Log.Error("Dust operation FAILED: reason: {0}, from: {1}, chat: {2}, by: {3}",
                    result, callerName, chatId, telegramId);
                break;
            default:
                Log.Information(
                    "Dust operation FAILED: reason: {0}, from: {1}, chat: {2}, by: {3}",
                    result, callerName, chatId, telegramId);
                break;
        }
    }

    public static T LogSuccessUsingItem<T>(this T maybeResult,
        long chatId,
        long telegramId,
        [CallerMemberName] string callerName = "unknownCaller") where T : Enum
    {
        Log.Information("Item usage SUCCESS from: {0}, Result: {1}, User: {2}, Chat: {3},",
            callerName, maybeResult, telegramId, chatId);
        return maybeResult;
    }
}
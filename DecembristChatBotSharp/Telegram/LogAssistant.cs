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

    public static LoreResult LogLore(this LoreResult result,
        long telegramId,
        long chatId,
        string key,
        string content = "-",
        [CallerMemberName] string callerName = "UnknownCaller")
    {
        switch (result)
        {
            case LoreResult.Success:
                Log.Information("Lore operation SUCCESS: from: {0}, key: {1}, content:{2}, chat: {3}, by: {4}",
                    callerName, key, content, chatId, telegramId);
                break;
            case LoreResult.Failed:
                Log.Error("Lore operation FAILED: reason: {0}, from: {1}, key: {2}, content:{3}, chat: {4}, by: {5}",
                    result, callerName, key, content, chatId, telegramId);
                break;
            default:
                Log.Information(
                    "Lore operation FAILED: reason: {0}, from: {1}, key: {2}, content:{3}, chat: {4}, by: {5}",
                    result, callerName, key, content, chatId, telegramId);
                break;
        }

        return result;
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
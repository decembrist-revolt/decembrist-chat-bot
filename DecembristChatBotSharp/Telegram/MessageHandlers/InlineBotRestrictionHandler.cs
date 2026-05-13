using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class InlineBotRestrictionHandler(
    BotClient botClient,
    RestrictedInlineBotRepository restrictedInlineBotRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        if (!parameters.ViaBotUsername.TryGetSome(out var username)) return false;

        var normalizedUsername = RestrictInlineBotCommandHandler.NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername)) return false;

        var (messageId, telegramId, chatId) = parameters;
        if (await adminUserRepository.IsAdmin((telegramId, chatId))) return false;
        if (!await restrictedInlineBotRepository.IsRestricted(chatId, normalizedUsername)) return false;

        await botClient.DeleteMessageAndLog(chatId, messageId,
            () => Log.Information("Deleted message from restricted inline bot {0} in chat {1}, user {2}",
                normalizedUsername, chatId, telegramId),
            ex => Log.Error(ex, "Failed to delete message from restricted inline bot {0} in chat {1}, user {2}",
                normalizedUsername, chatId, telegramId),
            cancelToken.Token);

        return true;
    }
}

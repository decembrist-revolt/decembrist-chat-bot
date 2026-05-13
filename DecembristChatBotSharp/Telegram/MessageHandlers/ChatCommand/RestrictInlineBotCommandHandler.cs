using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class RestrictInlineBotCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    RestrictedInlineBotRepository restrictedInlineBotRepository) : ICommandHandler
{
    private const string UnblockSubcommand = "unblock";

    public string Command => "/restrictinlinebot";
    public string Description => appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(
        Command, "Restrict or unblock inline bot usage in this chat");
    public CommandLevel CommandLevel => CommandLevel.Admin;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        var username = ParseUsername(text);
        var taskResult = username.Match(
            async botUsername => await HandleRestrict(text, botUsername, chatId, telegramId),
            () =>
            {
                Log.Warning("Bot username for {0} not set in chat {1}", Command, chatId);
                return Task.FromResult(unit);
            });

        return await Array(taskResult,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }

    private async Task<Unit> HandleRestrict(string text, string username, long chatId, long adminId)
    {
        var isUnblock = text.Contains(UnblockSubcommand, StringComparison.OrdinalIgnoreCase)
                        || text.Contains(ChatCommandHandler.DeleteSubcommand, StringComparison.OrdinalIgnoreCase);

        return isUnblock
            ? await DeleteRestrictedInlineBotAndLog(chatId, username, adminId)
            : await AddRestrictedInlineBotAndLog(chatId, username, adminId);
    }

    private async Task<Unit> AddRestrictedInlineBotAndLog(long chatId, string username, long adminId)
    {
        if (await restrictedInlineBotRepository.AddRestrictedInlineBot(chatId, username))
        {
            Log.Information("Added restricted inline bot {0} in chat {1} by {2}", username, chatId, adminId);
        }
        else
        {
            Log.Error("Restricted inline bot {0} not added in chat {1} by {2}", username, chatId, adminId);
        }

        return unit;
    }

    private async Task<Unit> DeleteRestrictedInlineBotAndLog(long chatId, string username, long adminId)
    {
        var isDelete = await restrictedInlineBotRepository.DeleteRestrictedInlineBot(chatId, username);
        if (isDelete)
        {
            Log.Information("Deleted restricted inline bot {0} in chat {1} by {2}", username, chatId, adminId);
        }
        else
        {
            Log.Warning("Restricted inline bot {0} in chat {1} was not deleted by {2}", username, chatId, adminId);
        }

        return unit;
    }

    private Option<string> ParseUsername(string text)
    {
        var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var username = args
            .Skip(1)
            .Select(NormalizeUsername)
            .FirstOrDefault(username => !string.IsNullOrWhiteSpace(username));

        return Optional(username);
    }

    public static string NormalizeUsername(string username) =>
        username.Trim().TrimStart('@').ToLowerInvariant();
}

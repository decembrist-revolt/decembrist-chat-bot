using System.Text.RegularExpressions;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatCommandHandler(Lazy<IList<ICommandHandler>> handlers, AccessLevelHandler accessLevelHandler)
{
    public const string DeleteSubcommand = "clear";

    public async Task<CommandResult> Do(ChatMessageHandlerParams parameters)
    {
        var command = parameters.Payload is TextPayload { Text: var text }
            ? text
            : throw new Exception("Payload is not a text payload");

        var maybeHandler = handlers.Value.Find(handler => MatchCommand(command, handler));

        return await maybeHandler
            .MapAsync(async handler => await accessLevelHandler.Do(handler, parameters))
            .IfNone(CommandResult.None);
    }

    private bool MatchCommand(string command, ICommandHandler handler)
    {
        var escaped = Regex.Escape(handler.Command);
        var pattern = $@"^{escaped}($|\s|@)";
        return Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase);
    }
}

public enum CommandResult
{
    None,
    Ok,
    NoItem,
    NoAdmin
}

[Flags]
public enum CommandLevel : byte
{
    None = 0,
    User = 1,
    Admin = 2,
    Item = 4,
    All = User | Admin | Item
}
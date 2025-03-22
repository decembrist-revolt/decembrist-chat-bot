using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ChatCommandHandler(Lazy<IList<ICommandHandler>> handlers)
{
    public async Task<CommandResult> Do(ChatMessageHandlerParams parameters)
    {
        var command = parameters.Payload is TextPayload { Text: var text }
            ? text
            : throw new Exception("Payload is not a text payload");
        var maybeHandler = handlers.Value.Find(handler => MatchCommand(command, handler));

        return await maybeHandler
            .MapAsync(handler => handler.Do(parameters))
            .Match(
                _ => CommandResult.Ok,
                () => CommandResult.None
            );
    }
    
    private bool MatchCommand(string command, ICommandHandler handler) => 
        command == handler.Command
        || command.StartsWith(handler.Command + ' ') 
        || command.StartsWith(handler.Command + '@');
}

public enum CommandResult
{
    None,
    Ok
}
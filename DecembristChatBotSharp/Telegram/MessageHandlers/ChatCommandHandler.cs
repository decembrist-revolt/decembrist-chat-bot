using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public class ChatCommandHandler(IEnumerable<ICommandHandler> handlers)
{
    public async Task<CommandResult> Do(ChatMessageHandlerParams parameters)
    {
        var command = parameters.Payload is TextPayload { Text: var text }
            ? text
            : throw new Exception("Payload is not a text payload");
        var maybeHandler = 
            Optional(handlers.Filter(handler => command.StartsWith(handler.Command)).FirstOrDefault());

        return await maybeHandler
            .MapAsync(handler => handler.Do(parameters))
            .Match(
                _ => CommandResult.Ok,
                () => CommandResult.None
            );
    }
}

public enum CommandResult
{
    None,
    Ok
}
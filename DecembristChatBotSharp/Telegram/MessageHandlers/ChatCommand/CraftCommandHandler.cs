using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class CraftCommandHandler :ICommandHandler
{
    public string Command => "/craft";
    public string Description => "craft items";
    public CommandLevel CommandLevel => CommandLevel.User;
    public Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        throw new NotImplementedException();
    }
}
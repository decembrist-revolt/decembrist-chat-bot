using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class DislikeCommandHandler(
    AppConfig appConfig) : ICommandHandler
{
    public string Command => "/dislike";
    public string Description => "Reply with this command to give the user a dislike";

    public Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        throw new NotImplementedException();
    }
}
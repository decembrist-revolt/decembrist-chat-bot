using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class AmuletCommandHandler : ICommandHandler
{
    public const string CommandKey = "/amulet";
    public string Command => CommandKey;

    public string Description =>
        "Protects the owner from /ban while in inventory. Passive purge /curse and /charm and destroy the amulet.";

    public CommandLevel CommandLevel => CommandLevel.None;
    public Task<Unit> Do(ChatMessageHandlerParams parameters) => Task.FromResult(unit);
}
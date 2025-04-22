using DecembristChatBotSharp.Mongo;
using Lamar;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class InventoryCommandHandler(
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    AppConfig appConfig,
    BotClient botClient
) : ICommandHandler
{
    public string Command => "/inventory";
    public string Description => "Show users items";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, _, chatId) = parameters;

        var lockSuccess = await lockRepository.TryAcquire(chatId, Command);
        if (!lockSuccess) return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        var url = await botClient.GetBotStartLink(PrivateMessageHandler.InventoryCommandSuffix + chatId);
        return await Array(
            messageAssistance.SendInviteToDirect(chatId, url, appConfig.ItemConfig.InviteInventoryMessage),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }
}
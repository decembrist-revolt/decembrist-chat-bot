using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using static DecembristChatBotSharp.Telegram.MessageHandlers.PrivateMessageHandler;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class InventoryCommandHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    CommandLockRepository lockRepository,
    BotClient botClient,
    ChatConfigService chatConfigService
) : ICommandHandler
{
    public string Command => "/inventory";

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(Command, "Show users items");

    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, _, chatId) = parameters;
        var maybeItemConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.ItemConfig);
        if (!maybeItemConfig.TryGetSome(out var itemConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(ItemConfig), Command);
        }

        var lockSuccess = await lockRepository.TryAcquire(chatId, Command);
        if (!lockSuccess) return await messageAssistance.CommandNotReady(chatId, messageId, Command);
        var url = await botClient.GetBotStartLink(GetCommandForChat(InventoryCommandSuffix, chatId));
        return await Array(
            messageAssistance.SendInviteToDirect(chatId, url, itemConfig.InviteInventoryMessage),
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }
}
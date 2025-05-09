using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.LoreHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types;
using static DecembristChatBotSharp.Service.ProfileService;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers;

[Singleton]
public class ProfileCallbackHandler(
    MessageAssistance messageAssistance,
    AppConfig appConfig,
    ProfileService profileService,
    LoreService loreService,
    InventoryService inventoryService)
{
    public const string Prefix = "profile";
    public const string LorViewCallback = "loreForChat";
    public const string CreateLoreCallback = "createLoreForChat";
    public const string DeleteLoreCallback = "deleteLoreForChat";
    public const string BackCallback = "goBack";

    public async Task<Unit> Do(string suffix, long chatId, CallbackQuery callbackQuery)
    {
        var messageId = callbackQuery.Message!.Id;
        var telegramId = callbackQuery.From.Id;

        return suffix switch
        {
            LorViewCallback => await SwitchToLore(messageId, telegramId, chatId),
            CreateLoreCallback => await SendRequestLoreKey(chatId, telegramId),
            DeleteLoreCallback => await SendRequestDelete(chatId, telegramId),
            PrivateMessageHandler.InventoryCommandSuffix => await SwitchToInventory(messageId, telegramId, chatId),
            BackCallback => await SwitchToWelcome(messageId, telegramId, chatId),
            _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
        };
    }

    private async Task<Unit> SwitchToWelcome(int messageId, long telegramId, long chatId)
    {
        var markup = await profileService.GetProfileMarkup(telegramId, chatId);
        var message = appConfig.MenuConfig.WelcomeMessage;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Unit> SwitchToInventory(int messageId, long telegramId, long chatId)
    {
        var markup = GetBackButton(chatId);
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Unit> SwitchToLore(int messageId, long telegramId, long chatId)
    {
        var markup = GetLoreMarkup(chatId);
        var message = appConfig.MenuConfig.LorDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private Task<Unit> SendRequestDelete(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.DeleteRequest,
            LoreService.GetLoreTag(LoreHandler.DeleteSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(PrivateMessageHandler), replyMarkup: loreService.GetKeyTip());
    }

    private Task<Unit> SendRequestLoreKey(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyRequest,
            LoreService.GetLoreTag(LoreHandler.KeySuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(PrivateMessageHandler), replyMarkup: loreService.GetKeyTip());
    }
}
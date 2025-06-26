using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.Enums;
using static DecembristChatBotSharp.Service.Buttons.ProfileButtons;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class ProfilePrivateCallbackHandler(
    CallbackService callbackService,
    MessageAssistance messageAssistance,
    LoreButtons loreButtons,
    AdminPanelButton adminPanelButton,
    AppConfig appConfig,
    ProfileButtons profileButtons,
    InventoryService inventoryService) : IPrivateCallbackHandler
{
    public const string PrefixKey = "Profile";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;

        if (!Enum.TryParse(suffix, true, out ProfileSuffix profileSuffix)) return unit;

        var taskResult = maybeParameters.MatchAsync(
            None: () => messageAssistance.SendCommandResponse(chatId, "OK", nameof(ProfilePrivateCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId)) return unit;

                return profileSuffix switch
                {
                    ProfileSuffix.Lore => await SwitchToLore(messageId, telegramId, targetChatId),
                    ProfileSuffix.Inventory => await SwitchToInventory(messageId, telegramId, targetChatId),
                    ProfileSuffix.AdminPanel => await SwitchToAdminPanel(messageId, telegramId, targetChatId),
                    ProfileSuffix.Back => await SwitchToWelcome(messageId, telegramId, targetChatId),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            }
        );
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SwitchToWelcome(int messageId, long telegramId, long chatId)
    {
        var markup = await profileButtons.GetProfileMarkup(telegramId, chatId);
        var message = appConfig.MenuConfig.WelcomeMessage;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SwitchToInventory(int messageId, long telegramId, long chatId)
    {
        var markup = GetBackButton(chatId);
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix,
            ParseMode.MarkdownV2);
    }

    private async Task<Unit> SwitchToLore(int messageId, long telegramId, long chatId)
    {
        var markup = loreButtons.GetLoreMarkup(chatId);
        var message = appConfig.MenuConfig.LoreDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SwitchToAdminPanel(int messageId, long telegramId, long chatId)
    {
        var markup = adminPanelButton.GetMarkup(chatId);
        var message = appConfig.MenuConfig.LoreDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }
}

public enum ProfileSuffix
{
    Lore,
    Inventory,
    AdminPanel,
    Back
}
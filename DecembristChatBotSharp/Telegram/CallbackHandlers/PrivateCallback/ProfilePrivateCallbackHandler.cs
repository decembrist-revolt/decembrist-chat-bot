using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
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
    ChatConfigService chatConfigService,
    ProfileButtons profileButtons,
    InventoryService inventoryService,
    MazeGameMapService mazeGameMapService,
    AdminUserRepository adminUserRepository) : IPrivateCallbackHandler
{
    public const string PrefixKey = "Profile";
    public string Prefix => PrefixKey;
    public const string ProfileTitle = "Ваш профиль в чате {0} \n\n{1}";

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;

        if (!Enum.TryParse(suffix, true, out ProfileSuffix profileSuffix)) return unit;

        var maybeMenuConfig = await chatConfigService.GetConfig(chatId, config => config.MenuConfig);
        if (!maybeMenuConfig.TryGetSome(out var menuConfig))
            return chatConfigService.LogNonExistConfig(unit, nameof(MenuConfig));

        var taskResult = maybeParameters.MatchAsync(
            None: () => messageAssistance.SendCommandResponse(chatId, "OK", nameof(ProfilePrivateCallbackHandler)),
            Some: async parameters =>
            {
                if (!callbackService.HasChatIdKey(parameters, out var targetChatId)) return unit;

                return profileSuffix switch
                {
                    ProfileSuffix.Lore => await SwitchToLore(messageId, telegramId, targetChatId, menuConfig),
                    ProfileSuffix.Inventory => await SwitchToInventory(messageId, telegramId, targetChatId),
                    ProfileSuffix.AdminPanel => await SwitchToAdminPanel(messageId, telegramId, targetChatId,
                        menuConfig),
                    ProfileSuffix.MazeMap => await SwitchToMazeMap(messageId, telegramId, targetChatId, menuConfig),
                    ProfileSuffix.BackMedia =>
                        await SendProfileMessage(messageId, telegramId, targetChatId, menuConfig),
                    ProfileSuffix.Back => await SwitchToWelcome(messageId, telegramId, targetChatId, menuConfig),
                    _ => throw new ArgumentOutOfRangeException(nameof(suffix), suffix, null)
                };
            }
        );
        return await Array(taskResult, messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix)).WhenAll();
    }

    private async Task<Unit> SwitchToWelcome(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        var markup = await profileButtons.GetProfileMarkup(telegramId, chatId);
        var message = menuConfig.WelcomeMessage;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SendProfileMessage(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        await messageAssistance.DeleteCommandMessage(telegramId, messageId, Prefix);
        var markup = await profileButtons.GetProfileMarkup(telegramId, chatId);
        var message = menuConfig.WelcomeMessage;
        return await messageAssistance.SendMessage(telegramId, message, Prefix, markup);
    }

    private async Task<Unit> SwitchToInventory(int messageId, long telegramId, long chatId)
    {
        var markup = GetBackButton(chatId);
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix,
            ParseMode.MarkdownV2);
    }

    private async Task<Unit> SwitchToLore(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        var markup = loreButtons.GetLoreMarkup(chatId);
        var message = menuConfig.LoreDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SwitchToAdminPanel(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        var markup = adminPanelButton.GetMarkup(chatId);
        var message = menuConfig.FilterDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SwitchNonMazeView(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        var markup = await profileButtons.GetProfileMarkup(telegramId, chatId);
        var message = menuConfig.NonMazeDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message, Prefix);
    }

    private async Task<Unit> SwitchToMazeMap(int messageId, long telegramId, long chatId, MenuConfig menuConfig)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));
        if (!isAdmin) return await messageAssistance.SendAdminOnlyMessage(telegramId, telegramId);
        var mapMediaOpt = await mazeGameMapService.GetFullMazeMapMedia(telegramId, chatId);
        return await mapMediaOpt.Match(media =>
            {
                var markup = GetBackFromMediaButton(chatId);
                return messageAssistance.EditMessageMediaAndLog(
                    telegramId,
                    messageId,
                    media,
                    Prefix,
                    markup
                );
            },
            () => SwitchNonMazeView(messageId, telegramId, chatId, menuConfig));
    }
}

public enum ProfileSuffix
{
    Lore,
    Inventory,
    AdminPanel,
    MazeMap,
    BackMedia,
    Back
}
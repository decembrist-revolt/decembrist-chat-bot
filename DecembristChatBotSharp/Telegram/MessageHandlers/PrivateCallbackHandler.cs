using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using static DecembristChatBotSharp.Service.ProfileService;
using static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class PrivateCallbackHandler(
    LoreService loreService,
    BotClient botClient,
    AppConfig appConfig,
    InventoryService inventoryService,
    MessageAssistance messageAssistance,
    ProfileService profileService,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> Do(CallbackQuery callbackQuery)
    {
        await botClient.AnswerCallbackQuery(callbackQuery.Id);

        var data = callbackQuery.Data;
        var messageId = callbackQuery.Message!.Id;
        var telegramId = callbackQuery.From.Id;

        return data switch
        {
            not null when data.Split("=") is [_, var chatIdText] &&
                          long.TryParse(chatIdText, out var targetChatId) => targetChatId switch
            {
                _ when data.Contains(BackCallback) => await SendWelcomeProfile(messageId, telegramId, targetChatId),
                _ when data.Contains(LorViewCallback) => await SendLorView(messageId, telegramId, targetChatId),
                _ when data.Contains(CreateLoreCallback) => await SendRequestLorKey(targetChatId, telegramId),
                _ when data.Contains(EditLoreCallback) => await SendRequestEdit(targetChatId, telegramId),
                _ when data.Contains(PrivateMessageHandler.InventoryCommandSuffix)
                    => await SendInventoryView(messageId, telegramId, targetChatId),
                _ => throw new ArgumentOutOfRangeException()
            },
            _ => await messageAssistance.SendCommandResponse(telegramId, "OK", nameof(PrivateMessageHandler))
        };
    }

    private async Task<Unit> SendWelcomeProfile(int messageId, long telegramId, long chatId)
    {
        var markup = await profileService.GetProfileMarkup(telegramId, chatId);
        var message = appConfig.MenuConfig.WelcomeMessage;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Unit> SendInventoryView(int messageId, long telegramId, long chatId)
    {
        var markup = new[] { WithCallbackData("Back", BackCallback + chatId) };
        var message = await inventoryService.GetInventory(chatId, telegramId);
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private async Task<Unit> SendLorView(int messageId, long telegramId, long chatId)
    {
        var markup = GetLoreMarkup(chatId);
        var message = appConfig.MenuConfig.LorDescription;
        return await messageAssistance.EditProfileMessage(telegramId, chatId, messageId, markup, message);
    }

    private Task<Unit> SendRequestEdit(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.EditRequest,
            LoreService.GetLoreTag(LoreReplyHandler.LorEditSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(PrivateMessageHandler), replyMarkup: loreService.GetKeyTip());
    }

    private Task<Unit> SendRequestLorKey(long targetChatId, long chatId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyRequest,
            LoreService.GetLoreTag(LoreReplyHandler.LorCreateSuffix, targetChatId));
        return messageAssistance.SendCommandResponse(
            chatId, message, nameof(PrivateMessageHandler), replyMarkup: loreService.GetKeyTip());
    }
}
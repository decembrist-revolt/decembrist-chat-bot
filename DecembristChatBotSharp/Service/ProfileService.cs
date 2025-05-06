using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ProfileService(
    AppConfig appConfig,
    BotClient botClient,
    LorUserRepository lorUserRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken)
{
    public const string LorViewCallback = "lorForChat=";
    public const string CreateLorCallback = "createLorForChat=";
    public const string EditLorCallback = "editLorForChat=";
    public const string BackCallback = "goBack=";

    public async Task<InlineKeyboardMarkup> GetProfileMarkup(long telegramId, long chatId)
    {
        var markup = new List<InlineKeyboardButton[]>();
        markup.Add([
            WithCallbackData("Inventory", PrivateMessageHandler.InventoryCommandSuffix + chatId),
        ]);
        var id = (telegramId, chatId);
        if (await lorUserRepository.IsLorUser(id) || await adminUserRepository.IsAdmin(id))
        {
            markup.Add([WithCallbackData("Lor", LorViewCallback + chatId),]);
        }

        return new InlineKeyboardMarkup(markup);
    }

    public async Task<Message> EditProfileMessage(
        long telegramId, long chatId, int messageId, InlineKeyboardMarkup replyMarkup, string text)
    {
        var chatTitle = await botClient.GetChatTitleOrId(chatId, cancelToken.Token);
        var message = string.Format(appConfig.MenuConfig.ProfileTitle, chatTitle, text);
        return await botClient.EditMessageText(telegramId, messageId, message,
            replyMarkup: replyMarkup,
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancelToken.Token);
    }
}
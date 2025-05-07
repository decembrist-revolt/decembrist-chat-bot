using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ProfileService(
    LoreUserRepository loreUserRepository,
    AdminUserRepository adminUserRepository)
{
    public const string LorViewCallback = "lorForChat=";
    public const string CreateLoreCallback = "createLorForChat=";
    public const string EditLoreCallback = "editLorForChat=";
    public const string BackCallback = "goBack=";

    public async Task<InlineKeyboardMarkup> GetProfileMarkup(long telegramId, long chatId)
    {
        var markup = new List<InlineKeyboardButton[]>();
        markup.Add([
            WithCallbackData("Inventory", PrivateMessageHandler.InventoryCommandSuffix + chatId),
        ]);
        var id = (telegramId, chatId);
        if (await loreUserRepository.IsLoreUser(id) || await adminUserRepository.IsAdmin(id))
        {
            markup.Add([WithCallbackData("Lor", LorViewCallback + chatId),]);
        }

        return new InlineKeyboardMarkup(markup);
    }

    public static InlineKeyboardMarkup GetLoreMarkup(long chatId) => new()
    {
        InlineKeyboard =
        [
            [WithCallbackData("Create Lore", CreateLoreCallback + chatId)],
            [WithCallbackData("Edit Lore", EditLoreCallback + chatId)],
            [WithCallbackData("Back", BackCallback + chatId)]
        ]
    };
}
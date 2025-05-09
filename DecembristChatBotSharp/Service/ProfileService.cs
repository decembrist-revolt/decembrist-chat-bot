using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Telegram.CallbackHandlers.ProfileCallbackHandler;
using static DecembristChatBotSharp.Telegram.MessageHandlers.PrivateMessageHandler;
using static Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ProfileService(
    LoreUserRepository loreUserRepository,
    AdminUserRepository adminUserRepository)
{
    public static string GetProfileCallback(string callback, long chatId) =>
        Prefix + SplitSymbol + callback + SplitSymbol + chatId;

    public async Task<InlineKeyboardMarkup> GetProfileMarkup(long telegramId, long chatId)
    {
        var markup = new List<InlineKeyboardButton[]>();
        markup.Add([
            WithCallbackData("Inventory", GetProfileCallback(InventoryCommandSuffix, chatId)),
        ]);
        var id = (telegramId, chatId);
        if (await loreUserRepository.IsLoreUser(id) || await adminUserRepository.IsAdmin(id))
        {
            markup.Add([WithCallbackData("Lor", GetProfileCallback(LorViewCallback, chatId)),]);
        }

        return new InlineKeyboardMarkup(markup);
    }

    public static InlineKeyboardMarkup GetLoreMarkup(long chatId) => new()
    {
        InlineKeyboard =
        [
            [WithCallbackData("Create Lore", GetProfileCallback(CreateLoreCallback, chatId))],
            [WithCallbackData("Delete Lore", GetProfileCallback(DeleteLoreCallback, chatId))],
            GetBackButton(chatId)
        ]
    };

    public static InlineKeyboardButton[] GetBackButton(long chatId) =>
    [
        WithCallbackData("Back", GetProfileCallback(BackCallback, chatId))
    ];
}
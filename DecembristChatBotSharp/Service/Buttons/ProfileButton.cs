using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;
using static DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback.ProfilePrivateCallbackHandler;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class ProfileButtons(
    LoreUserRepository loreUserRepository,
    AdminUserRepository adminUserRepository)
{
    public async Task<InlineKeyboardMarkup> GetProfileMarkup(long telegramId, long chatId)
    {
        var markup = new List<InlineKeyboardButton[]>();
        markup.Add([GetProfileButton("Inventory", chatId, ProfileSuffix.Inventory)]);

        var id = (telegramId, chatId);
        if (await loreUserRepository.IsLoreUser(id) || await adminUserRepository.IsAdmin(id))
        {
            markup.Add([GetProfileButton("Lore", chatId, ProfileSuffix.Lore)]);
        }

        return new InlineKeyboardMarkup(markup);
    }

    public static InlineKeyboardButton GetProfileButton(string name, long chatId, ProfileSuffix suffix)
    {
        var callback = GetCallback(PrefixKey, suffix, (ChatIdParameter, chatId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }

    public static InlineKeyboardButton GetBackButton(long chatId)
    {
        var callback = GetCallback(PrefixKey, ProfileSuffix.Back, (ChatIdParameter, chatId));
        return InlineKeyboardButton.WithCallbackData("Back Profile", callback);
    }
}
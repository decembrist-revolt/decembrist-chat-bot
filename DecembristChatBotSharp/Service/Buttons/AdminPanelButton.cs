using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class AdminPanelButton
{
    public InlineKeyboardMarkup GetMarkup(long chatId)
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [GetAdminPanelButton("Create filter record", chatId, FilterSuffix.Create)],
                [GetAdminPanelButton("Delete filter record", chatId, FilterSuffix.Delete)],
                [ProfileButtons.GetBackButton(chatId)]
            ]
        };
    }

    private static InlineKeyboardButton GetAdminPanelButton(string name, long chatId, FilterSuffix suffix)
    {
        var callback = GetCallback(FilterCallbackHandler.PrefixKey, suffix, (ChatIdParameter, chatId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
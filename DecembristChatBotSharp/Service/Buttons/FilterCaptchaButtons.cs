using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class FilterCaptchaButtons()
{
    public InlineKeyboardMarkup GetMarkup()
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [GetChatConfigButton("Забанить", FilterAdminDecision.Bun)],
                [GetChatConfigButton("Разбанить", FilterAdminDecision.UnBun)],
            ]
        };
    }

    private static InlineKeyboardButton GetChatConfigButton(string name, FilterAdminDecision suffix)
    {
        var callback = CallbackService.GetCallback(ChatConfigCallbackHandler.PrefixKey, suffix);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
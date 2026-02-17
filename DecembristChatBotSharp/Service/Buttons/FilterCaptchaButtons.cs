using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
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
                [GetChatConfigButton("Забанить", FilterAdminDecision.Ban)],
                [GetChatConfigButton("Разбанить", FilterAdminDecision.UnBan)],
            ]
        };
    }

    private static InlineKeyboardButton GetChatConfigButton(string name, FilterAdminDecision suffix)
    {
        var callback = CallbackService.GetCallback(FilterAdminCallbackHandler.PrefixKey, suffix);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
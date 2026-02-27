using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class FilterCaptchaButtons()
{
    public InlineKeyboardMarkup GetMarkup(long telegramId)
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [
                    GetChatConfigButton("Забанить", telegramId, FilterAdminDecision.Ban),
                    GetChatConfigButton("Разбанить", telegramId, FilterAdminDecision.UnBan)
                ],
            ]
        };
    }

    private static InlineKeyboardButton GetChatConfigButton(string name, long telegramId, FilterAdminDecision suffix)
    {
        var callback = CallbackService.GetCallback(
            FilterCaptchaCallbackHandler.PrefixKey, suffix, (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
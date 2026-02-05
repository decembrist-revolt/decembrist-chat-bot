using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

public class GlobalAdminButton()
{
    public InlineKeyboardMarkup GetMarkup()
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [GetGlobalAdminButton("Add Disabled Chat Config", GlobalAdminSuffix.AddDisabledChatConfig)],
                [GetGlobalAdminButton("Add Enabled Chat Config", GlobalAdminSuffix.AddEnabledChatConfig)],
                [GetGlobalAdminButton("Remove Chat Config", GlobalAdminSuffix.RemoveChatConfig)],
            ]
        };
    }

    private static InlineKeyboardButton GetGlobalAdminButton(string name, GlobalAdminSuffix suffix)
    {
        var callback = CallbackService.GetCallback(name, suffix);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
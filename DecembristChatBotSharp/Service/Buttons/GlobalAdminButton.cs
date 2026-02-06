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
                [GetGlobalAdminButton("Enable Chat Config", GlobalAdminSuffix.EnableChatConfig)],
                [GetGlobalAdminButton("Disable Chat Config", GlobalAdminSuffix.DisableChatConfig)],
                [GetGlobalAdminButton("Remove Chat Config", GlobalAdminSuffix.RemoveChatConfig)],
            ]
        };
    }

    private static InlineKeyboardButton GetGlobalAdminButton(string name, GlobalAdminSuffix suffix)
    {
        var callback = CallbackService.GetCallback(ChatConfigCallbackHandler.PrefixKey, suffix);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
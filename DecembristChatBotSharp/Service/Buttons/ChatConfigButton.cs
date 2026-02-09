using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

public class ChatConfigButton()
{
    public InlineKeyboardMarkup GetMarkup()
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [GetChatConfigButton("Add Disabled Chat Config", GlobalAdminSuffix.AddDisabledChatConfig)],
                [GetChatConfigButton("Enable Chat Config", GlobalAdminSuffix.EnableChatConfig)],
                [GetChatConfigButton("Disable Chat Config", GlobalAdminSuffix.DisableChatConfig)],
                [GetChatConfigButton("Remove Chat Config", GlobalAdminSuffix.RemoveChatConfig)],
                [GetChatConfigButton("Show Chat Config List", GlobalAdminSuffix.ChatConfigList)],
            ]
        };
    }

    private static InlineKeyboardButton GetChatConfigButton(string name, GlobalAdminSuffix suffix)
    {
        var callback = CallbackService.GetCallback(ChatConfigCallbackHandler.PrefixKey, suffix);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
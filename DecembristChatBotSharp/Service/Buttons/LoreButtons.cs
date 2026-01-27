using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class LoreButtons()
{
    public InlineKeyboardMarkup GetLoreMarkup(long chatId)
    {
        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [GetLoreButton("Create Lore", chatId, LoreSuffix.Create)],
                [GetLoreButton("Delete Lore", chatId, LoreSuffix.Delete)],
                [GetLoreListButton("Show lore records", chatId, 0)],
                [ProfileButtons.GetBackButton(chatId)]
            ]
        };
    }

    public InlineKeyboardMarkup GetLoreListPrivateMarkup(long chatId, int currentOffset, int totalCount)
    {
        var markup = new List<InlineKeyboardButton>();
        var limit = ListService.ListRowLimit;
        if (currentOffset > 0)
        {
            markup.Add(GetLoreListButton("⬅️ Prev", chatId, currentOffset - limit));
        }

        if (currentOffset + limit < totalCount)
        {
            markup.Add(GetLoreListButton("Next ➡️", chatId, currentOffset + limit));
        }

        return new InlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                markup,
                [ProfileButtons.GetProfileButton("Back Lore", chatId, ProfileSuffix.Lore)]
            ]
        };
    }

    private static InlineKeyboardButton GetLoreButton(string name, long chatId, LoreSuffix suffix)
    {
        var callback = GetCallback(LorePrivateCallbackHandler.PrefixKey, suffix, (ChatIdParameter, chatId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }

    private static InlineKeyboardButton GetLoreListButton(string name, long chatId, int startIndex)
    {
        (string, object)[] parameters = [(IndexStartParameter, startIndex), (ChatIdParameter, chatId)];
        var callback = GetCallback(LorePrivateCallbackHandler.PrefixKey, LoreSuffix.List, parameters);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
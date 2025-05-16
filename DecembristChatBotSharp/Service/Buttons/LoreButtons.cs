using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class LoreButtons(AppConfig appConfig)
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


    public InlineKeyboardMarkup GetLoreListChatMarkup(int totalCount, int currentOffset = 0)
    {
        var markup = new List<InlineKeyboardButton>();
        var limit = appConfig.LoreListConfig.RowLimit;
        if (currentOffset > 0)
        {
            markup.Add(GetLoreListChatButton("⬅️ Prev", currentOffset - limit));
        }

        if (currentOffset + limit < totalCount)
        {
            markup.Add(GetLoreListChatButton("Next ➡️", currentOffset + limit));
        }

        return new InlineKeyboardMarkup(markup);
    }

    public InlineKeyboardMarkup GetLoreListPrivateMarkup(long chatId, int currentOffset, int totalCount)
    {
        var markup = new List<InlineKeyboardButton>();
        var limit = appConfig.LoreListConfig.RowLimit;
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

    private static InlineKeyboardButton GetLoreListChatButton(string name, int startIndex)
    {
        var loreListParameter = (IndexStartParameter, startIndex);
        var callback = GetCallback(LoreCallbackHandler.PrefixKey, LoreChatSuffix.List, loreListParameter);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
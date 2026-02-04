using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class ListButtons
{
    public InlineKeyboardMarkup GetListChatMarkup(int totalCount, ListType listType, int currentOffset = 0)
    {
        var markup = new List<InlineKeyboardButton>();
        const int limit = ListService.ListRowLimit;
        if (currentOffset > 0)
        {
            markup.Add(GetListChatButton("⬅️ Prev", listType, currentOffset - limit));
        }

        if (currentOffset + limit < totalCount)
        {
            markup.Add(GetListChatButton("Next ➡️", listType, currentOffset + limit));
        }

        return new InlineKeyboardMarkup(markup);
    }

    private static InlineKeyboardButton GetListChatButton(string name, ListType listType, int startIndex)
    {
        var loreListParameter = (IndexStartParameter, startIndex);
        var callback = GetCallback(ListCallbackHandler.PrefixKey, listType, loreListParameter);
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
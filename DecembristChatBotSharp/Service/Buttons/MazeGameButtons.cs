using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;
using static DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback.MazeGameMoveCallbackHandler;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class MazeGameButtons(AppConfig appConfig)
{
    public InlineKeyboardMarkup CreateMazeKeyboard(long chatId)
    {
        var exitCallback = GetCallback(MazeGameExitCallbackHandler.PrefixKey, MazeGameExitCallbackHandler.PrefixKey,
            (ChatIdParameter, chatId));

        return new InlineKeyboardMarkup([
            [GetMoveButton("⬆️", chatId, MazeDirection.Up)],
            [
                GetMoveButton("⬅️", chatId, MazeDirection.Left),
                GetMoveButton("➡️", chatId, MazeDirection.Right),
            ],
            [GetMoveButton("⬇️", chatId, MazeDirection.Down)],
            [InlineKeyboardButton.WithCallbackData(" ")],
            [InlineKeyboardButton.WithCallbackData("🚪 Выйти", exitCallback)]
        ]);
    }

    private static InlineKeyboardButton GetMoveButton(string name, long chatId, MazeDirection suffix)
    {
        var callback = GetCallback(PrefixKey, suffix, (ChatIdParameter, chatId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
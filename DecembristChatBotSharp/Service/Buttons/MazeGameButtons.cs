using DecembristChatBotSharp.Entity;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Entity.MazeDirection;
using static DecembristChatBotSharp.Service.CallbackService;
using static DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback.MazeGameMoveCallbackHandler;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class MazeGameButtons(AppConfig appConfig)
{
    public InlineKeyboardMarkup GetMazeKeyboard(long chatId)
    {
        return new InlineKeyboardMarkup([
            [
                GetMoveButton("⬆️x3", chatId, Up, 3),
                GetMoveButton("⬆️x2", chatId, Up, 2),
                GetMoveButton("⬆️", chatId, Up, 1),
            ],
            [
                GetMoveButton("⬅️x3", chatId, MazeDirection.Left, 3),
                GetMoveButton("➡️x3", chatId, MazeDirection.Right, 3),
                GetMoveButton("⬅️x2", chatId, MazeDirection.Left, 2),
                GetMoveButton("➡️x2", chatId, MazeDirection.Right, 2),
                GetMoveButton("⬅️", chatId, MazeDirection.Left, 1),
                GetMoveButton("➡️", chatId, MazeDirection.Right, 1),
            ],
            [
                GetMoveButton("⬇️x3", chatId, Down, 3),
                GetMoveButton("⬇️x2", chatId, Down, 2),
                GetMoveButton("⬇️", chatId, Down, 1),
            ],

            [InlineKeyboardButton.WithCallbackData(" ")],
            [
                InlineKeyboardButton.WithCallbackData("🚪 Выйти",
                    GetCallback(PrefixKey, ExitSuffix, (ChatIdParameter, chatId)))
            ]
        ]);
    }

    private static InlineKeyboardButton GetMoveButton(string name, long chatId, MazeDirection suffix, int countSteps)
    {
        var callback = GetCallback(PrefixKey, suffix, (ChatIdParameter, chatId),
            (StepsCountParameter, countSteps));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}
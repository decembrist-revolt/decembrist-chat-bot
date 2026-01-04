using DecembristChatBotSharp.Entity;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameUiService(AppConfig appConfig)
{
    public string FormatInventoryText(MazePlayerInventory inventory)
    {
        return string.Format(
            appConfig.MazeGameConfig.InventoryTextTemplate,
            inventory.Swords,
            inventory.Shields,
            inventory.Shovels,
            inventory.ViewExpanders
        );
    }

    public InlineKeyboardMarkup CreateMazeKeyboard(long chatId, int messageId)
    {
        var upCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Up}");
        var downCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Down}");
        var leftCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Left}");
        var rightCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Right}");
        var exitCallback = GetCallback<string>("MazeExit", $"{chatId}_{messageId}");

        return new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("⬆️", upCallback)],
            [
                InlineKeyboardButton.WithCallbackData("⬅️", leftCallback),
                InlineKeyboardButton.WithCallbackData("➡️", rightCallback)
            ],
            [InlineKeyboardButton.WithCallbackData("⬇️", downCallback)],
            [InlineKeyboardButton.WithCallbackData(" ")],
            [InlineKeyboardButton.WithCallbackData("🚪 Выйти", exitCallback)]
        ]);
    }
}


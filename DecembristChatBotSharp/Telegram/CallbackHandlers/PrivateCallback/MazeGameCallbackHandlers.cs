using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class MazeGameMoveCallbackHandler(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameViewService mazeGameViewService,
    MemberItemRepository memberItemRepository,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : IPrivateCallbackHandler
{
    public const string PrefixKey = "MazeMove";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, privateChatId, telegramId, _, queryId, _) = queryParameters;

        if (!TryParseMoveData(suffix, out var chatId, out var messageId, out var direction))
        {
            Log.Warning("Failed to parse maze move data from suffix: {0}", suffix);
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
                "Ошибка обработки хода", showAlert: true);
        }

        var moved = await mazeGameService.MovePlayer(chatId, messageId, telegramId, direction);

        if (!moved)
        {
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
                "Невозможно переместиться в этом направлении", showAlert: false);
        }

        // Answer callback
        await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
            "Ход сделан", showAlert: false);

        // Check if game finished
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(chatId, messageId));
        await gameOpt.MatchAsync(
            async game =>
            {
                if (game.IsFinished && game.WinnerId == telegramId)
                {
                    // Player won!
                    _ = await HandleWinner(chatId, telegramId, privateChatId, messageId);
                }
                else
                {
                    // Schedule view update with 3 second delay
                    mazeGameViewService.ScheduleViewUpdate(chatId, messageId, telegramId);
                }
                return unit;
            },
            () => Task.FromResult(unit));

        return unit;
    }

    private async Task<Unit> HandleWinner(long chatId, long telegramId, long privateChatId, int messageId)
    {
        Log.Information("Player {0} won maze game in chat {1}", telegramId, chatId);

        // Give 5 boxes
        await memberItemRepository.AddMemberItem(chatId, telegramId, MemberItemType.Box, null, 5);

        // Send private message
        await botClient.SendMessageAndLog(
            privateChatId,
            "🎉 Поздравляем! Вы первым нашли выход из лабиринта!\n\nВы получили 5 коробок! 📦📦📦📦📦",
            _ => Log.Information("Sent winner message to player {0}", telegramId),
            ex => Log.Error(ex, "Failed to send winner message to player {0}", telegramId),
            cancelToken.Token
        );

        // Render and send full maze to chat
        var fullMazeImage = await mazeGameService.RenderFullMaze(chatId, messageId);
        if (fullMazeImage != null)
        {
            using var stream = new MemoryStream(fullMazeImage, false);
            var username = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
            
            await botClient.SendPhotoAndLog(
                chatId,
                stream,
                $"🎉 {username} первым нашел выход из лабиринта и получил 5 коробок!\n\nФинальная карта лабиринта с позициями всех участников:",
                _ => Log.Information("Sent final maze image to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send final maze image to chat {0}", chatId),
                cancelToken.Token
            );
        }
        else
        {
            // Fallback to text message if maze rendering fails
            var username = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);
            await botClient.SendMessageAndLog(
                chatId,
                $"🎉 {username} первым нашел выход из лабиринта и получил 5 коробок!",
                _ => Log.Information("Announced maze winner in chat {0}", chatId),
                ex => Log.Error(ex, "Failed to announce maze winner in chat {0}", chatId),
                cancelToken.Token
            );
        }

        return unit;
    }


    private bool TryParseMoveData(string suffix, out long chatId, out int messageId, out MazeDirection direction)
    {
        chatId = 0;
        messageId = 0;
        direction = MazeDirection.Up;

        try
        {
            var parts = suffix.Split('_');
            if (parts.Length < 3) return false;

            if (!long.TryParse(parts[0], out chatId)) return false;
            if (!int.TryParse(parts[1], out messageId)) return false;
            if (!int.TryParse(parts[2], out var directionInt)) return false;

            direction = (MazeDirection)directionInt;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[Singleton]
public class MazeGameExitCallbackHandler(
    MessageAssistance messageAssistance) : IPrivateCallbackHandler
{
    public const string PrefixKey = "MazeExit";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, _, privateChatId, telegramId, _, queryId, _) = queryParameters;

        Log.Information("Player {0} exited maze game", telegramId);

        return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
            "Вы вышли из игры. Используйте кнопку 'Вступить' в чате чтобы вернуться.", showAlert: true);
    }
}


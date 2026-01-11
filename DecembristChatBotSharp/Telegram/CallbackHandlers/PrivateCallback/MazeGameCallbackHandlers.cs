using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class MazeGameMoveCallbackHandler(
    AppConfig appConfig,
    BotClient botClient,
    CallbackService callbackService,
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
        var (_, suffix, privateChatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;

        if (!Enum.TryParse(suffix, true, out MazeDirection direction) || maybeParameters.IsNone) return unit;
        var parameters = maybeParameters.ValueUnsafe();

        if (!callbackService.HasChatIdKey(parameters, out var targetChatId))
        {
            Log.Warning("Maze move parameters do not contain chatId");
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
                "Ошибка обработки хода", showAlert: true);
        }

        var moved = await mazeGameService.MovePlayer(targetChatId, messageId, telegramId, direction);
        if (moved == MazeMoveResult.KeyboardNotFound)
        {
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
                appConfig.MazeConfig.KeyboardIncorrectMessage, showAlert: true);
        }

        if (moved == MazeMoveResult.InvalidMove)
        {
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
                "Невозможно переместиться в этом направлении", showAlert: false);
        }

        // Answer callback
        await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix,
            "Ход сделан", showAlert: false);

        // Check if game finished
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(targetChatId));
        await gameOpt.MatchAsync(
            async game =>
            {
                if (game.IsFinished && game.WinnerId == telegramId)
                {
                    // Player won!
                    _ = await HandleWinner(targetChatId, telegramId, privateChatId);
                }
                else
                {
                    // Schedule view update with 3 second delay
                    mazeGameViewService.ScheduleViewUpdate(targetChatId, telegramId);
                }

                return unit;
            },
            () => Task.FromResult(unit));

        return unit;
    }

    private async Task<Unit> HandleWinner(long chatId, long telegramId, long privateChatId)
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
        var fullMazeImage = await mazeGameService.RenderFullMaze(chatId);
        if (fullMazeImage != null)
        {
            await using var stream = new MemoryStream(fullMazeImage, false);
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

        await mazeGameService.RemoveGameAndPlayers(chatId);
        return unit;
    }
}

[Singleton]
public class MazeGameExitCallbackHandler(
    AppConfig appConfig,
    MessageAssistance messageAssistance) : IPrivateCallbackHandler
{
    public const string PrefixKey = "MazeExit";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, _, privateChatId, telegramId, messageId, queryId, _) = queryParameters;
        await messageAssistance.DeleteCommandMessage(privateChatId, messageId, Prefix);
        Log.Information("Player {0} exited maze game", telegramId);
        return await messageAssistance.SendMessage(privateChatId, appConfig.MazeConfig.GameExitMessage, Prefix);
    }
}
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;

[Singleton]
public class MazeGameMoveCallbackHandler(
    ChatConfigService chatConfigService,
    BotClient botClient,
    CallbackService callbackService,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameViewService mazeGameViewService,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    MongoDatabase db,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : IPrivateCallbackHandler
{
    public const string PrefixKey = "MazeMove";
    public const string ExitSuffix = "MazeExit";
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

        if (suffix == ExitSuffix) return await SendGameExit(privateChatId, telegramId, messageId);
        var steps = callbackService.HasStepsCountKey(parameters, out var stepsCount)
            ? stepsCount
            : 1;

        var maybeConfig = await chatConfigService.GetConfig(targetChatId, config => config.MazeConfig);
        if (!maybeConfig.TryGetSome(out var mazeConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(MazeConfig), Prefix);
        }

        var moved = await mazeGameService.MovePlayer(targetChatId, messageId, telegramId, direction, steps);
        var answer = moved switch
        {
            MazeMoveResult.Success => "Ход сделан",
            MazeMoveResult.InvalidMove => "Невозможно переместиться в этом направлении",
            MazeMoveResult.KeyboardNotFound => mazeConfig.KeyboardIncorrectMessage,
            MazeMoveResult.PartialSuccess => "Ход сделан частично",
            _ => "Ошибка обработки хода"
        };
        if (moved != MazeMoveResult.Success && moved != MazeMoveResult.PartialSuccess)
        {
            return await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix, answer, showAlert: true);
        }

        await messageAssistance.AnswerCallbackQuery(queryId, privateChatId, Prefix, answer, showAlert: false);

        // Check if game finished
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(targetChatId));
        return await gameOpt.MatchAsync(
            async game =>
            {
                if (game.IsFinished && game.WinnerId == telegramId)
                {
                    // Player won!
                    _ = await HandleWinner(targetChatId, telegramId, privateChatId, mazeConfig);
                }
                else
                {
                    // Schedule view update with 3 second delay
                    mazeGameViewService.ScheduleViewUpdate(targetChatId, telegramId, mazeConfig);
                }

                return unit;
            },
            () => unit);
    }

    private async Task<Unit> SendGameExit(long privateChatId, long telegramId, int messageId)
    {
        await messageAssistance.DeleteCommandMessage(privateChatId, messageId, Prefix);
        Log.Information("Player {0} exited maze game", telegramId);
        return await messageAssistance.SendMessage(privateChatId, "Вы вышли из игры, вы можете перезайти из чата",
            Prefix);
    }

    private async Task<Unit> HandleWinner(long chatId, long telegramId, long privateChatId,
        Entity.Configs.MazeConfig mazeConfig)
    {
        Log.Information("Player {0} won maze game in chat {1}", telegramId, chatId);

        using var session = await db.OpenSession();
        session.StartTransaction();

        var boxReward = mazeConfig.WinnerBoxReward;
        var success =
            await memberItemRepository.AddMemberItem(chatId, telegramId, MemberItemType.Box, session, boxReward);
        if (!success)
        {
            await session.TryAbort(cancelToken.Token);
            Log.Error("Failed to give maze winner boxes to player {0} in chat {1}", telegramId, chatId);
        }
        else
        {
            // Log to history
            await historyLogRepository.LogItem(
                chatId, telegramId, MemberItemType.Box, boxReward, MemberItemSourceType.MazeGame, session);

            await session.TryCommit(cancelToken.Token);
        }

        await messageAssistance.SendMessage(
            privateChatId,
            $"🎉 Поздравляем! Вы первым нашли выход из лабиринта!\n\nВы получили {boxReward} коробок!",
            Prefix
        );

        var fullMazeImage = await mazeGameService.RenderFullMaze(chatId);
        var username = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);

        if (fullMazeImage != null)
        {
            await using var stream = new MemoryStream(fullMazeImage, false);
            await botClient.SendPhotoAndLog(
                chatId,
                stream,
                $"🎉 {username} первым нашел выход из лабиринта и получил {boxReward} коробок!\n\nФинальная карта лабиринта с позициями всех участников:",
                _ => Log.Information("Sent final maze image to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send final maze image to chat {0}", chatId),
                cancelToken.Token
            );
        }
        else
        {
            await messageAssistance.SendMessage(
                chatId,
                $"🎉 {username} первым нашел выход из лабиринта и получил {boxReward} коробок!",
                Prefix
            );
        }

        await mazeGameService.NotifyAllPlayer(chatId,
            $"{username} первым нашел выход из лабиринта,\nИгра окончена! Спасибо всем за участие. Лабиринт будет удален.");
        await mazeGameService.RemoveGameAndPlayers(chatId);

        return unit;
    }
}
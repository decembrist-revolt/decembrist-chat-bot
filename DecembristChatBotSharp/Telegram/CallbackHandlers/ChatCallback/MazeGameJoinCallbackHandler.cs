using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class MazeGameJoinCallbackHandler(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameUiService mazeGameUiService,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : IChatCallbackHandler
{
    public const string PrefixKey = "MazeJoin";
    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, _, chatId, telegramId, messageId, queryId, _) = queryParameters;

        var joinResult = await mazeGameService.JoinGame(chatId, messageId, telegramId);

        return await joinResult.MatchAsync(
            async player =>
            {
                Log.Information("Player {0} joined maze game in chat {1}", telegramId, chatId);

                // Answer callback
                await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix,
                    "Вы вступили в игру! Проверьте личные сообщения для управления.", showAlert: false);

                // Send controls to private chat
                return await SendGameControls(telegramId, chatId, messageId, player);
            },
            async () =>
            {
                Log.Warning("Failed to join maze game for player {0} in chat {1}", telegramId, chatId);
                return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix,
                    "Не удалось вступить в игру. Возможно игра уже завершена.", showAlert: true);
            });
    }

    private async Task<Unit> SendGameControls(long telegramId, long chatId, int messageId, MazeGamePlayer player)
    {
        var welcomeMessage = $"🎮 Добро пожаловать в лабиринт!\n\n" +
                           $"Ваш цвет: {player.Color}\n" +
                           $"Радиус видимости: {player.ViewRadius} (квадрат 7×7)\n\n" +
                           $"Используйте кнопки для перемещения.\n" +
                           $"Найдите зелёный выход!\n\n" +
                           $"🗡️ Меч: атаковать игрока\n" +
                           $"🛡️ Щит: защита от меча\n" +
                           $"⛏️ Лопата: пробить стену\n" +
                           $"🔭 Бинокль: +1 к радиусу видимости";

        // Отправляем приветственное сообщение
        await botClient.SendMessageAndLog(
            telegramId,
            welcomeMessage,
            _ => Log.Information("Sent maze welcome to player {0}", telegramId),
            ex => Log.Error(ex, "Failed to send maze welcome to player {0}", telegramId),
            cancelToken.Token
        );

        // Отправляем начальное фото с клавиатурой
        var viewImage = await mazeGameService.RenderPlayerView(chatId, messageId, telegramId);
        if (viewImage != null)
        {
            using var stream = new MemoryStream(viewImage, false);
            
            var inventoryText = mazeGameUiService.FormatInventoryText(player.Inventory);
            var keyboard = mazeGameUiService.CreateMazeKeyboard(chatId, messageId);

            await botClient.SendPhotoAndLog(
                telegramId,
                stream,
                inventoryText,
                async msg =>
                {
                    await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                        new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                        msg.MessageId
                    );
                    Log.Information("Sent initial maze view to player {0}", telegramId);
                },
                ex => Log.Error(ex, "Failed to send initial maze view to player {0}", telegramId),
                cancelToken.Token,
                keyboard
            );
        }

        return unit;
    }
}


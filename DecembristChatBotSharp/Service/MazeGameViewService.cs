using System.Collections.Concurrent;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot.Types.ReplyMarkups;
using static DecembristChatBotSharp.Service.CallbackService;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameViewService(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    CancellationTokenSource cancelToken)
{
    private readonly ConcurrentDictionary<(long chatId, int messageId, long telegramId), Timer> _pendingUpdates = new();
    private const int DelaySeconds = 3;

    public void ScheduleViewUpdate(long chatId, int messageId, long telegramId)
    {
        var key = (chatId, messageId, telegramId);

        // Отменяем предыдущий таймер если есть
        if (_pendingUpdates.TryRemove(key, out var existingTimer))
        {
            existingTimer.Dispose();
        }

        // Создаём новый таймер на 3 секунды
        var timer = new Timer(
            async _ => await SendViewUpdate(chatId, messageId, telegramId),
            null,
            TimeSpan.FromSeconds(DelaySeconds),
            Timeout.InfiniteTimeSpan
        );

        _pendingUpdates[key] = timer;
    }

    private async Task SendViewUpdate(long chatId, int messageId, long telegramId)
    {
        var key = (chatId, messageId, telegramId);
        
        // Удаляем таймер из словаря
        if (_pendingUpdates.TryRemove(key, out var timer))
        {
            timer.Dispose();
        }

        var playerOpt = await mazeGameRepository.GetPlayer(new MazeGamePlayer.CompositeId(chatId, messageId, telegramId));

        await playerOpt.MatchAsync(
            async player =>
            {
                // Удаляем предыдущее фото если есть
                if (player.LastPhotoMessageId.HasValue)
                {
                    try
                    {
                        await botClient.DeleteMessageAndLog(
                            telegramId,
                            player.LastPhotoMessageId.Value,
                            () => Log.Information("Deleted previous maze photo for player {0}", telegramId),
                            ex => Log.Warning(ex, "Failed to delete previous photo message for player {0}", telegramId),
                            cancelToken.Token
                        );
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to delete previous photo message {0} for player {1}", 
                            player.LastPhotoMessageId.Value, telegramId);
                    }
                }

                // Отправляем новое фото с клавиатурой
                var viewImage = await mazeGameService.RenderPlayerView(chatId, messageId, telegramId);
                if (viewImage != null)
                {
                    using var stream = new MemoryStream(viewImage, false);
                    
                    var inventoryText = "🎒 Инвентарь: " +
                                      "🗡️ " + player.Inventory.Swords + " " +
                                      "🛡️ " + player.Inventory.Shields + " " +
                                      "⛏️ " + player.Inventory.Shovels + " " +
                                      "🔭 " + player.Inventory.ViewExpanders;

                    // Создаём inline клавиатуру
                    var upCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Up}");
                    var downCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Down}");
                    var leftCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Left}");
                    var rightCallback = GetCallback<string>("MazeMove", $"{chatId}_{messageId}_{(int)MazeDirection.Right}");
                    var exitCallback = GetCallback<string>("MazeExit", $"{chatId}_{messageId}");

                    var keyboard = new InlineKeyboardMarkup([
                        [InlineKeyboardButton.WithCallbackData("⬆️", upCallback)],
                        [
                            InlineKeyboardButton.WithCallbackData("⬅️", leftCallback),
                            InlineKeyboardButton.WithCallbackData("➡️", rightCallback)
                        ],
                        [InlineKeyboardButton.WithCallbackData("⬇️", downCallback)],
                        [InlineKeyboardButton.WithCallbackData(" ")],
                        [InlineKeyboardButton.WithCallbackData("🚪 Выйти", exitCallback)]
                    ]);

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
                            Log.Information("Sent updated maze view to player {0}", telegramId);
                        },
                        ex => Log.Error(ex, "Failed to send maze view to player {0}", telegramId),
                        cancelToken.Token,
                        keyboard
                    );
                }

                return unit;
            },
            () => Task.FromResult(unit));
    }
}


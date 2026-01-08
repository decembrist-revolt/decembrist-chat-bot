using System.Collections.Concurrent;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameViewService(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameButtons mazeGameButtons,
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
            _ => SendViewUpdate(chatId, messageId, telegramId),
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
        if (_pendingUpdates.TryRemove(key, out var timer)) await timer.DisposeAsync();

        var playerOpt =
            await mazeGameRepository.GetPlayer(new MazeGamePlayer.CompositeId(chatId, messageId, telegramId));

        await playerOpt.MatchAsync(async player =>
        {
            // Удаляем предыдущее фото если есть
            if (player.LastPhotoMessageId.HasValue)
            {
                // await botClient.DeleteMessageAndLog(
                //     telegramId,
                //     player.LastPhotoMessageId.Value,
                //     () => Log.Information("Deleted previous maze photo for player {0}", telegramId),
                //     ex => Log.Warning(ex, "Failed to delete previous photo message for player {0}", telegramId),
                //     cancelToken.Token
                // );
            }

            // Отправляем новое фото с клавиатурой
            var viewImage = await mazeGameService.RenderPlayerView(chatId, messageId, telegramId);
            if (viewImage != null)
            {
                using var stream = new MemoryStream(viewImage, false);

                var inventoryText = mazeGameButtons.FormatInventoryText(player.Inventory);
                var keyboard = mazeGameButtons.CreateMazeKeyboard(chatId, messageId);

                await botClient.EditMessageMediaAndLog(
                    chatId: telegramId,
                    messageId: player.LastPhotoMessageId.Value,
                    media: new InputMediaPhoto(new InputFileStream(stream))
                    {
                        Caption = inventoryText
                    },
                    async msg =>
                    {
                        await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                            new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                            msg.MessageId
                        );
                        Log.Information("Sent updated maze view to player {0}", telegramId);
                    },
                    ex => Log.Error(ex, "Failed to send maze view to player {0}", telegramId),
                    cancelToken: cancelToken.Token,
                    replyMarkup: keyboard
                );

                // await botClient.SendPhotoAndLog(
                //     telegramId,
                //     stream,
                //     inventoryText,
                //     async msg =>
                //     {
                //         await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                //             new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                //             msg.MessageId
                //         );
                //         Log.Information("Sent updated maze view to player {0}", telegramId);
                //     },
                //     ex => Log.Error(ex, "Failed to send maze view to player {0}", telegramId),
                //     cancelToken.Token,
                //     keyboard
                // );
            }

            return unit;
        }, () => Task.FromResult(unit));
    }
}
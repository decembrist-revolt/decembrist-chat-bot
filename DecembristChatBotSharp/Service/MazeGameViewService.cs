using System.Collections.Concurrent;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service.Buttons;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Serilog;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameViewService(
    AppConfig appConfig,
    BotClient botClient,
    MessageAssistance messageAssistance,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    MazeGameButtons mazeGameButtons,
    CancellationTokenSource cancelToken)
{
    private readonly ConcurrentDictionary<MazeGamePlayer.CompositeId, Timer> _pendingUpdates = new();

    public void ScheduleViewUpdate(long chatId, long telegramId)
    {
        var key = (chatId, telegramId);

        // Отменяем предыдущий таймер если есть
        if (_pendingUpdates.TryRemove(key, out var existingTimer))
        {
            existingTimer.Dispose();
        }

        // Создаём новый таймер на 3 секунды
        var timer = new Timer(
            _ => SendViewUpdate(chatId, telegramId),
            null,
            TimeSpan.FromSeconds(appConfig.MazeConfig.MoveDelaySeconds),
            Timeout.InfiniteTimeSpan
        );

        _pendingUpdates[key] = timer;
    }

    private async Task SendViewUpdate(long chatId, long telegramId)
    {
        var key = (chatId, telegramId);

        // Удаляем таймер из словаря
        if (_pendingUpdates.TryRemove(key, out var timer)) await timer.DisposeAsync();

        var playerOpt =
            await mazeGameRepository.GetPlayer(new MazeGamePlayer.CompositeId(chatId, telegramId));

        await playerOpt.MatchAsync(async player =>
            {
                var viewImage = await mazeGameService.RenderPlayerView(chatId, telegramId);
                if (viewImage == null) return unit;

                await using var stream = new MemoryStream(viewImage, false);
                var inventoryText = FormatInventoryText(player.Inventory);
                var keyboard = mazeGameButtons.GetMazeKeyboard(chatId);

                if (player.LastPhotoMessageId.HasValue)
                {
                    return await messageAssistance.EditMessageMediaAndLog(telegramId, player.LastPhotoMessageId.Value,
                        new InputMediaPhoto(new InputFileStream(stream))
                        {
                            Caption = inventoryText
                        }, nameof(MazeGameViewService), keyboard);
                }

                return await SendMazeKeyboardPhoto(chatId, telegramId, stream, inventoryText, keyboard);
            },
            () => messageAssistance.SendMessage(telegramId, appConfig.MazeConfig.GameNotFoundMessage,
                nameof(MazeGameViewService)));
    }

    public string FormatInventoryText(MazePlayerInventory inventory) =>
        string.Format(
            appConfig.MazeConfig.InventoryTextTemplate,
            inventory.Swords,
            inventory.Shields,
            inventory.Shovels,
            inventory.ViewExpanders
        );

    private async Task<Unit> SendMazeKeyboardPhoto(
        long chatId, long telegramId, MemoryStream stream, string inventoryText, InlineKeyboardMarkup keyboard) =>
        await botClient.SendPhotoAndLog(
            telegramId,
            stream,
            inventoryText,
            async msg =>
            {
                await mazeGameRepository.UpdatePlayerLastPhotoMessageId(
                    new MazeGamePlayer.CompositeId(chatId, telegramId),
                    msg.MessageId
                );
                Log.Information("Sent updated maze view to player {0}", telegramId);
            },
            ex => Log.Error(ex, "Failed to send maze view to player {0}", telegramId),
            cancelToken.Token,
            keyboard
        );
}
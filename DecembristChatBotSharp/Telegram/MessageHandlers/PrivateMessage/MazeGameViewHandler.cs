using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using SkiaSharp;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.PrivateMessage;

[Singleton]
public class MazeGameViewHandler(
    BotClient botClient,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken)
{
    private const int CellSize = 10;
    private const int MazeSize = 128;

    public async Task<Unit> SendFullMazeMap(long privateChatId, long telegramId, long targetChatId)
    {
        // Проверяем что пользователь админ
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, targetChatId));
        if (!isAdmin)
        {
            await botClient.SendMessageAndLog(
                privateChatId,
                "❌ У вас нет прав администратора для этого чата",
                _ => { },
                ex => Log.Error(ex, "Failed to send admin check message"),
                cancelToken.Token
            );
            return unit;
        }

        // Находим активную игру
        var activeGameOpt = await FindActiveGameForChat(targetChatId);

        await activeGameOpt.MatchAsync(
            async game =>
            {
                // Рендерим полную карту с всеми игроками
                var fullMapImage = await RenderFullMazeMap(game, targetChatId);
                
                if (fullMapImage != null)
                {
                    using var stream = new MemoryStream(fullMapImage, false);
                    
                    var caption = $"🗺️ Полная карта лабиринта\nЧат: {targetChatId}\n" +
                                $"Игра началась: {game.CreatedAt:HH:mm:ss}\n" +
                                $"Статус: {(game.IsFinished ? "Завершена" : "Активна")}";

                    await botClient.SendPhotoAndLog(
                        privateChatId,
                        stream,
                        caption,
                        _ => Log.Information("Sent full maze map to admin {0} for chat {1}", telegramId, targetChatId),
                        ex => Log.Error(ex, "Failed to send full maze map to admin {0}", telegramId),
                        cancelToken.Token,
                        null
                    );
                }
                
                return unit;
            },
            async () =>
            {
                await botClient.SendMessageAndLog(
                    privateChatId,
                    $"❌ Нет активной игры лабиринт для чата {targetChatId}",
                    _ => { },
                    ex => Log.Error(ex, "Failed to send no game message"),
                    cancelToken.Token
                );
                return unit;
            });

        return unit;
    }

    private async Task<Option<MazeGame>> FindActiveGameForChat(long chatId)
    {
        return await mazeGameRepository.GetActiveGameForChat(chatId);
    }

    private async Task<byte[]?> RenderFullMazeMap(MazeGame game, long chatId)
    {
        var imageWidth = MazeSize * CellSize;
        var imageHeight = MazeSize * CellSize;

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var wallPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
        using var pathPaint = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Fill };
        using var exitPaint = new SKPaint { Color = SKColors.Green, Style = SKPaintStyle.Fill };
        using var chestPaint = new SKPaint { Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill };

        // Рисуем весь лабиринт
        for (var row = 0; row < MazeSize; row++)
        {
            for (var col = 0; col < MazeSize; col++)
            {
                var x = col * CellSize;
                var y = row * CellSize;

                switch (game.Maze[row, col])
                {
                    case 1: // Wall
                        canvas.DrawRect(x, y, CellSize, CellSize, wallPaint);
                        break;
                    case 2: // Path
                        canvas.DrawRect(x, y, CellSize, CellSize, pathPaint);
                        break;
                    case 3: // Exit
                        canvas.DrawRect(x, y, CellSize, CellSize, exitPaint);
                        break;
                    case 4: // Chest
                        canvas.DrawRect(x, y, CellSize, CellSize, chestPaint);
                        break;
                }
            }
        }

        // Рисуем всех игроков
        var players = await mazeGameRepository.GetAllPlayersInGame(chatId, game.Id.MessageId);
        foreach (var player in players.Where(p => p.IsAlive))
        {
            var (row, col) = player.Position;
            var x = col * CellSize;
            var y = row * CellSize;

            var color = SKColor.Parse(player.Color);
            using var playerPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, CellSize, CellSize, playerPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}


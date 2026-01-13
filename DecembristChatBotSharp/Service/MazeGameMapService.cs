using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using SkiaSharp;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameMapService(
    AppConfig appConfig,
    MazeGameService mazeGameService,
    MazeGameRepository mazeGameRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken)
{
    private const int CellSize = 10;
    private const int MazeSize = 128;

    public async Task<Option<InputMediaPhoto>> GetFullMazeMapMedia(long telegramId, long targetChatId)
    {
        var isAdmin = await adminUserRepository.IsAdmin((telegramId, targetChatId));
        if (!isAdmin) return None;

        var activeGameOpt = mazeGameService.FindActiveGameForChat(targetChatId);

        return await activeGameOpt.MatchAsync(
            async game =>
            {
                var fullMapImage = await RenderFullMazeMap(game, targetChatId);
                if (fullMapImage == null) return None;

                var stream = new MemoryStream(fullMapImage, false);
                var caption = string.Format(appConfig.MenuConfig.MazeDescription, targetChatId,
                    game.CreatedAt.ToString("HH:mm:ss"),
                    game.IsFinished ? "Завершена" : "Активна");

                var inputMedia = new InputMediaPhoto(new InputFileStream(stream))
                {
                    Caption = caption
                };

                return Some(inputMedia);
            },
            () => None
        );
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
        using var exitPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
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
        var players = await mazeGameRepository.GetAllPlayersInGame(chatId);
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
using Lamar;
using SkiaSharp;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeRendererService(MazeGeneratorService mazeGenerator)
{
    private const int CellSize = 10; // размер одной ячейки в пикселях
    private const int MazeSize = 128;

    /// <summary>
    /// Renders a maze to PNG image.
    /// </summary>
    private byte[] RenderMazeToPng(int[,] maze, List<(int row, int col)>? solution = null)
    {
        var imageWidth = MazeSize * CellSize;
        var imageHeight = MazeSize * CellSize;

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;

        // Fill background with white
        canvas.Clear(SKColors.White);

        // Create paints
        using (var wallPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill })
        using (var pathPaint = new SKPaint
                   { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Fill }) // Light gray for paths
        using (var exitPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
        using (var chestPaint = new SKPaint
                   { Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill }) // Gold color for chests
        using (var solutionPaint = new SKPaint { Color = new SKColor(255, 0, 0, 180), Style = SKPaintStyle.Fill })
        {
            // Draw maze
            for (var row = 0; row < MazeSize; row++)
            {
                for (var col = 0; col < MazeSize; col++)
                {
                    var x = col * CellSize;
                    var y = row * CellSize;

                    switch (maze[row, col])
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
                        // case 0: empty space - already white background
                    }
                }
            }

            // Draw solution path if provided
            if (solution is { Count: > 0 })
            {
                foreach (var (row, col) in solution)
                {
                    var x = col * CellSize;
                    var y = row * CellSize;
                    canvas.DrawRect(x, y, CellSize, CellSize, solutionPaint);
                }
            }
        }

        // Save to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Saves maze as PNG file to the specified path.
    /// </summary>

    /// <summary>
    /// Renders a custom maze (useful for testing or rendering pre-generated mazes).
    /// </summary>
    public byte[] RenderCustomMaze(int[,] maze, List<(int row, int col)>? solution = null)
    {
        return RenderMazeToPng(maze, solution);
    }

    /// <summary>
    /// Renders full maze with all players' positions marked by their colors.
    /// </summary>
    public byte[] RenderMazeWithPlayers(int[,] maze, List<((int row, int col) position, string color)> players)
    {
        var imageWidth = MazeSize * CellSize;
        var imageHeight = MazeSize * CellSize;

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using (var wallPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill })
        using (var pathPaint = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Fill })
        using (var exitPaint = new SKPaint { Color = new SKColor(0, 255, 0), Style = SKPaintStyle.Fill }) // Green exit
        using (var chestPaint = new SKPaint { Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill })
        {
            // Draw maze
            for (var row = 0; row < MazeSize; row++)
            {
                for (var col = 0; col < MazeSize; col++)
                {
                    var x = col * CellSize;
                    var y = row * CellSize;

                    switch (maze[row, col])
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

            // Draw all players with their colors
            foreach (var (position, colorHex) in players)
            {
                var (row, col) = position;
                var x = col * CellSize;
                var y = row * CellSize;

                var color = SKColor.Parse(colorHex);
                using var playerPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x, y, CellSize, CellSize, playerPaint);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
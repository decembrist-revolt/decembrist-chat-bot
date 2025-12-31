using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using SkiaSharp;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGameService(
    MazeGeneratorService mazeGenerator,
    MazeGameRepository mazeGameRepository,
    AppConfig appConfig,
    Random random)
{
    private const int MazeSize = 128;
    private const int CellSize = 10;

    private static readonly string[] PlayerColors = new[]
    {
        "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#FF00FF", "#00FFFF",
        "#FF8000", "#8000FF", "#00FF80", "#FF0080", "#80FF00", "#0080FF",
        "#FF4040", "#40FF40", "#4040FF", "#FFFF40", "#FF40FF", "#40FFFF"
    };

    public async Task<Option<MazeGame>> CreateGame(long chatId, int messageId)
    {
        var maze = mazeGenerator.GenerateMaze();
        var exitPosition = FindExitPosition(maze);
        
        if (exitPosition == (-1, -1))
        {
            Log.Error("Failed to find exit in generated maze");
            return None;
        }

        var game = new MazeGame(
            new MazeGame.CompositeId(chatId, messageId),
            maze,
            DateTime.UtcNow,
            exitPosition,
            false,
            null
        );

        var created = await mazeGameRepository.CreateGame(game);
        return created ? Some(game) : None;
    }

    public async Task<Option<MazeGamePlayer>> JoinGame(long chatId, int messageId, long telegramId)
    {
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(chatId, messageId));
        
        return await gameOpt.MatchAsync(
            async game =>
            {
                if (game.IsFinished)
                {
                    Log.Warning("Player {0} tried to join finished game", telegramId);
                    return None;
                }

                // Get existing players to check color usage
                var existingPlayers = await mazeGameRepository.GetAllPlayersInGame(chatId, messageId);
                var usedColors = existingPlayers.Map(p => p.Color).ToHashSet();
                
                // Find available color
                var availableColor = PlayerColors.FirstOrDefault(c => !usedColors.Contains(c)) ?? PlayerColors[0];

                // Generate random spawn position at edge
                var spawnPosition = GenerateEdgeSpawnPosition(game.Maze, existingPlayers);

                var player = new MazeGamePlayer(
                    new MazeGamePlayer.CompositeId(chatId, messageId, telegramId),
                    spawnPosition,
                    spawnPosition,
                    availableColor,
                    appConfig.MazeConfig.DefaultViewRadius,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    true,
                    MazePlayerInventory.Empty,
                    false,
                    null // LastPhotoMessageId
                );

                var added = await mazeGameRepository.AddPlayer(player);
                return added ? Some(player) : None;
            },
            () => Task.FromResult<Option<MazeGamePlayer>>(None));
    }

    public async Task<bool> MovePlayer(long chatId, int messageId, long telegramId, MazeDirection direction)
    {
        var playerId = new MazeGamePlayer.CompositeId(chatId, messageId, telegramId);
        var playerOpt = await mazeGameRepository.GetPlayer(playerId);
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(chatId, messageId));

        return await playerOpt.MatchAsync(
            async player => await gameOpt.MatchAsync(
                async game =>
                {
                    if (!player.IsAlive || game.IsFinished) return false;

                    var (currentRow, currentCol) = player.Position;
                    var (newRow, newCol) = ApplyDirection(currentRow, currentCol, direction);

                    // Check bounds
                    if (newRow < 0 || newRow >= MazeSize || newCol < 0 || newCol >= MazeSize)
                        return false;

                    var cellType = game.Maze[newRow, newCol];

                    // Handle wall movement with shovel
                    if (cellType == 1) // Wall
                    {
                        if (player.Inventory.Shovels > 0)
                        {
                            // Use shovel to break wall
                            game.Maze[newRow, newCol] = 2; // Convert wall to path
                            await mazeGameRepository.UpdateMaze(game.Id, game.Maze);
                            
                            var newInventory = player.Inventory with { Shovels = player.Inventory.Shovels - 1 };
                            await mazeGameRepository.UpdatePlayerInventory(playerId, newInventory);
                            await mazeGameRepository.UpdatePlayerPosition(playerId, (newRow, newCol));
                            return true;
                        }
                        return false; // Can't move through wall
                    }

                    // Check for other players at target position
                    var allPlayers = await mazeGameRepository.GetAllPlayersInGame(chatId, messageId);
                    var targetPlayer = allPlayers.FirstOrDefault(p => p.Position == (newRow, newCol) && p.IsAlive);

                    if (targetPlayer != null)
                    {
                        // Another player is here
                        if (player.Inventory.Swords > 0)
                        {
                            // Attack with all swords vs all shields
                            var attackerSwords = player.Inventory.Swords;
                            var defenderShields = targetPlayer.Inventory.Shields;
                            
                            // Обменять все мечи на все щиты
                            var swordsUsed = Math.Min(attackerSwords, defenderShields);
                            var remainingSwords = attackerSwords - swordsUsed;
                            var remainingShields = defenderShields - swordsUsed;
                            
                            if (remainingSwords > 0)
                            {
                                // У атакующего остались мечи - защитник погибает
                                await mazeGameRepository.KillPlayer(targetPlayer.Id);
                                await Task.Delay(100); // Small delay before respawn
                                await mazeGameRepository.RevivePlayer(targetPlayer.Id);
                                
                                // У атакующего сгорают ВСЕ мечи (использовал все для атаки)
                                var attackerInventory = player.Inventory with { Swords = 0 };
                                var defenderInventory = targetPlayer.Inventory with { Shields = 0 };
                                
                                await mazeGameRepository.UpdatePlayerInventory(playerId, attackerInventory);
                                await mazeGameRepository.UpdatePlayerInventory(targetPlayer.Id, defenderInventory);
                                await mazeGameRepository.UpdatePlayerPosition(playerId, (newRow, newCol));
                                return true;
                            }
                            else
                            {
                                // Щитов было >= мечей - атака отражена, атакующий не двигается
                                var attackerInventory = player.Inventory with { Swords = 0 };
                                var defenderInventory = targetPlayer.Inventory with { Shields = remainingShields };
                                
                                await mazeGameRepository.UpdatePlayerInventory(playerId, attackerInventory);
                                await mazeGameRepository.UpdatePlayerInventory(targetPlayer.Id, defenderInventory);
                                return true; // Move registered but position unchanged
                            }
                        }
                        return false; // Can't move to occupied cell without sword
                    }

                    // Handle chest pickup
                    if (cellType == 4) // Chest
                    {
                        var itemType = (MazeItemType)random.Next(4); // Теперь 4 типа предметов
                        var newInventory = itemType switch
                        {
                            MazeItemType.Sword => player.Inventory with { Swords = player.Inventory.Swords + 1 },
                            MazeItemType.Shield => player.Inventory with { Shields = player.Inventory.Shields + 1 },
                            MazeItemType.Shovel => player.Inventory with { Shovels = player.Inventory.Shovels + 1 },
                            MazeItemType.ViewExpander => player.Inventory with { ViewExpanders = player.Inventory.ViewExpanders + 1 },
                            _ => player.Inventory
                        };

                        game.Maze[newRow, newCol] = 2; // Remove chest
                        await mazeGameRepository.UpdateMaze(game.Id, game.Maze);
                        await mazeGameRepository.UpdatePlayerInventory(playerId, newInventory);
                    }

                    // Move player
                    await mazeGameRepository.UpdatePlayerPosition(playerId, (newRow, newCol));

                    // Check if reached exit
                    if ((newRow, newCol) == game.ExitPosition)
                    {
                        await mazeGameRepository.FinishGame(game.Id, telegramId);
                    }

                    return true;
                },
                () => Task.FromResult(false)),
            () => Task.FromResult(false));
    }

    public async Task<byte[]?> RenderPlayerView(long chatId, int messageId, long telegramId)
    {
        var playerId = new MazeGamePlayer.CompositeId(chatId, messageId, telegramId);
        var playerOpt = await mazeGameRepository.GetPlayer(playerId);
        var gameOpt = await mazeGameRepository.GetGame(new MazeGame.CompositeId(chatId, messageId));

        return await playerOpt.MatchAsync(
            async player => await gameOpt.MatchAsync(
                async game =>
                {
                    await mazeGameRepository.MarkPlayerUpdateSent(playerId);
                    return RenderPlayerViewInternal(game, player, await mazeGameRepository.GetAllPlayersInGame(chatId, messageId));
                },
                () => Task.FromResult<byte[]?>(null)),
            () => Task.FromResult<byte[]?>(null));
    }

    private byte[]? RenderPlayerViewInternal(MazeGame game, MazeGamePlayer currentPlayer, List<MazeGamePlayer> allPlayers)
    {
        var (playerRow, playerCol) = currentPlayer.Position;
        // Базовый радиус + бонус от ViewExpanders (каждый добавляет +1)
        var radius = currentPlayer.ViewRadius + currentPlayer.Inventory.ViewExpanders;
        var viewSize = radius * 2 + 1;

        var imageWidth = viewSize * CellSize;
        var imageHeight = viewSize * CellSize;

        using var surface = SKSurface.Create(new SKImageInfo(imageWidth, imageHeight));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black); // Unknown areas are black

        using var wallPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
        using var pathPaint = new SKPaint { Color = new SKColor(200, 200, 200), Style = SKPaintStyle.Fill };
        using var exitPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var chestPaint = new SKPaint { Color = new SKColor(255, 215, 0), Style = SKPaintStyle.Fill };

        // Draw visible area
        for (var dr = -radius; dr <= radius; dr++)
        {
            for (var dc = -radius; dc <= radius; dc++)
            {
                var mazeRow = playerRow + dr;
                var mazeCol = playerCol + dc;

                if (mazeRow < 0 || mazeRow >= MazeSize || mazeCol < 0 || mazeCol >= MazeSize)
                    continue;

                var viewRow = dr + radius;
                var viewCol = dc + radius;
                var x = viewCol * CellSize;
                var y = viewRow * CellSize;

                var cellType = game.Maze[mazeRow, mazeCol];

                switch (cellType)
                {
                    case 0: // Empty
                        // Already black background
                        break;
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

        // Draw all visible players
        foreach (var player in allPlayers.Where(p => p.IsAlive))
        {
            var (otherRow, otherCol) = player.Position;
            var dr = otherRow - playerRow;
            var dc = otherCol - playerCol;

            // Check if player is in visible range
            if (Math.Abs(dr) <= radius && Math.Abs(dc) <= radius)
            {
                var viewRow = dr + radius;
                var viewCol = dc + radius;
                var x = viewCol * CellSize;
                var y = viewRow * CellSize;

                var color = SKColor.Parse(player.Color);
                using var playerPaint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x, y, CellSize, CellSize, playerPaint);
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private (int row, int col) FindExitPosition(int[,] maze)
    {
        for (var i = 0; i < MazeSize; i++)
        {
            for (var j = 0; j < MazeSize; j++)
            {
                if (maze[i, j] == 3) return (i, j);
            }
        }
        return (-1, -1);
    }

    private (int row, int col) GenerateEdgeSpawnPosition(int[,] maze, List<MazeGamePlayer> existingPlayers)
    {
        var usedPositions = existingPlayers.Map(p => p.SpawnPosition).ToHashSet();
        var attempts = 0;
        const int maxAttempts = 100;

        while (attempts < maxAttempts)
        {
            var edge = random.Next(4); // 0=top, 1=right, 2=bottom, 3=left
            int row, col;

            switch (edge)
            {
                case 0: // Top edge
                    row = 0;
                    col = random.Next(MazeSize);
                    break;
                case 1: // Right edge
                    row = random.Next(MazeSize);
                    col = MazeSize - 1;
                    break;
                case 2: // Bottom edge
                    row = MazeSize - 1;
                    col = random.Next(MazeSize);
                    break;
                default: // Left edge
                    row = random.Next(MazeSize);
                    col = 0;
                    break;
            }

            // Check if position is valid (not wall) and not used
            if (maze[row, col] != 1 && !usedPositions.Contains((row, col)))
            {
                return (row, col);
            }

            attempts++;
        }

        // Fallback to first available edge position
        for (var i = 0; i < MazeSize; i++)
        {
            if (maze[0, i] != 1 && !usedPositions.Contains((0, i))) return (0, i);
            if (maze[MazeSize - 1, i] != 1 && !usedPositions.Contains((MazeSize - 1, i))) return (MazeSize - 1, i);
            if (maze[i, 0] != 1 && !usedPositions.Contains((i, 0))) return (i, 0);
            if (maze[i, MazeSize - 1] != 1 && !usedPositions.Contains((i, MazeSize - 1))) return (i, MazeSize - 1);
        }

        return (0, 0); // Ultimate fallback
    }

    private (int row, int col) ApplyDirection(int row, int col, MazeDirection direction)
    {
        return direction switch
        {
            MazeDirection.Up => (row - 1, col),
            MazeDirection.Down => (row + 1, col),
            MazeDirection.Left => (row, col - 1),
            MazeDirection.Right => (row, col + 1),
            _ => (row, col)
        };
    }
}


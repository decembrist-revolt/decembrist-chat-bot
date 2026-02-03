using DecembristChatBotSharp.Entity.Configs;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGeneratorService(Random random, AppConfig appConfig)
{
    /// <summary>
    /// Generates a 128x128 maze where 0 is empty space, 1 is a wall, 2 is a path, 3 is the exit, and 4 is a chest.
    /// Classic maze with one solution path from edge to exit.
    /// Exit is placed randomly (not at center, not at edge) with guaranteed long path.
    /// Chests are placed randomly throughout the maze (frequency configurable via MazeConfig.ChestFrequency) without blocking paths.
    /// Outer edge is clear for starting area.
    /// </summary>
    /// <param name="mazeConfig"></param>
    public (int[,], (int exitRow, int exitCol)) GenerateMaze(MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var maze = new int[mazeSize, mazeSize];

        // Initialize all cells as walls (1)
        for (var i = 0; i < mazeSize; i++)
        {
            for (var j = 0; j < mazeSize; j++)
            {
                maze[i, j] = 1;
            }
        }

        var centerRow = mazeSize / 2;
        var centerCol = mazeSize / 2;

        // Step 1: Clear the outer edge (starting area)
        ClearOuterEdge(maze, mazeConfig);

        // Step 2: Generate classic maze using recursive backtracking from center
        GenerateClassicMazeFromCenter(maze, centerRow, centerCol, mazeConfig);

        // Step 3: Choose random exit position (not too close to edge, not at center)
        var (exitRow, exitCol) = ChooseRandomExitPosition(centerRow, centerCol, mazeConfig);

        // Step 4: Ensure there's a long path from edge to exit
        EnsurePathFromEdgeToExit(maze, exitRow, exitCol, mazeConfig);

        // Step 5: Mark exit as 3x3 area (9 cells total)
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var r = exitRow + dr;
                var c = exitCol + dc;
                if (r >= 0 && r < mazeSize && c >= 0 && c < mazeSize)
                {
                    maze[r, c] = 3;
                }
            }
        }

        // Step 6: Fill all accessible paths with value 2 (will preserve 3 for exit)
        FloodFillPaths(maze, mazeConfig);

        // Step 7: Place chests randomly (1 per ~50 cells, value 4)
        PlaceChestsInMaze(maze, mazeConfig);

        return (maze, (exitRow, exitCol));
    }

    /// <summary>
    /// Chooses a random exit position that is:
    /// - Not too close to edge (at least 40 cells away)
    /// - Not at the exact center
    /// - In a position that allows for a long path
    /// </summary>
    private (int row, int col) ChooseRandomExitPosition(int centerRow, int centerCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        const int minDistanceFromEdge = 40; // Increased from 20 to 40 for longer paths
        const int maxDistanceFromEdge = 50;

        // Choose random position in a ring between minDistance and maxDistance from edge
        var side = random.Next(4); // 0=top-left quadrant bias, 1=top-right, 2=bottom-right, 3=bottom-left

        int exitRow, exitCol;

        switch (side)
        {
            case 0: // Top-left quadrant
                exitRow = random.Next(minDistanceFromEdge, centerRow - 20);
                exitCol = random.Next(minDistanceFromEdge, centerCol - 20);
                break;
            case 1: // Top-right quadrant
                exitRow = random.Next(minDistanceFromEdge, centerRow - 20);
                exitCol = random.Next(centerCol + 20, mazeSize - minDistanceFromEdge);
                break;
            case 2: // Bottom-right quadrant
                exitRow = random.Next(centerRow + 20, mazeSize - minDistanceFromEdge);
                exitCol = random.Next(centerCol + 20, mazeSize - minDistanceFromEdge);
                break;
            default: // Bottom-left quadrant
                exitRow = random.Next(centerRow + 20, mazeSize - minDistanceFromEdge);
                exitCol = random.Next(minDistanceFromEdge, centerCol - 20);
                break;
        }

        return (exitRow, exitCol);
    }

    /// <summary>
    /// Places chests randomly in the maze (value 4)
    /// Frequency is configurable via MazeConfig.ChestFrequency (default: 1 chest per 50 path cells)
    /// Chests are placed on paths but don't block them
    /// </summary>
    private void PlaceChestsInMaze(int[,] maze, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        // Count total path cells (value 2)
        var pathCellCount = 0;
        for (var i = 0; i < mazeSize; i++)
        {
            for (var j = 0; j < mazeSize; j++)
            {
                if (maze[i, j] == 2)
                    pathCellCount++;
            }
        }

        // Calculate number of chests based on config (ChestFrequency from appsettings.json)
        var chestCount = pathCellCount / mazeConfig.ChestFrequency;

        // Calculate dynamic minimum distance based on chest density
        // More chests = smaller distance, fewer chests = larger distance
        var minDistance = chestCount switch
        {
            < 100 => 15, // Очень мало сундуков - большое расстояние
            < 300 => 10, // Мало сундуков - среднее расстояние
            < 600 => 7, // Среднее количество - небольшое расстояние
            < 1000 => 5, // Много сундуков - маленькое расстояние
            _ => 3 // Очень много сундуков - минимальное расстояние
        };

        // Collect all valid path positions
        var validPathPositions = new List<(int row, int col)>();
        for (var i = 0; i < mazeSize; i++)
        {
            for (var j = 0; j < mazeSize; j++)
            {
                if (maze[i, j] == 2)
                {
                    // Check if this is not a critical path cell (has multiple exits)
                    // A cell is safe for chest if it has at least 2 adjacent path cells
                    var adjacentPaths = CountAdjacentPaths(maze, i, j, mazeConfig);
                    if (adjacentPaths >= 2)
                    {
                        validPathPositions.Add((i, j));
                    }
                }
            }
        }

        // Randomly place chests
        var placedChests = 0;
        var shuffledPositions = validPathPositions.OrderBy(x => random.Next()).ToList();

        foreach (var (row, col) in shuffledPositions)
        {
            if (placedChests >= chestCount)
                break;

            // Ensure chests are not too close to each other (dynamic distance)
            var tooClose = false;

            // Use Manhattan distance for efficiency instead of square check
            for (var i = 0; i < shuffledPositions.Count && !tooClose; i++)
            {
                var (otherRow, otherCol) = shuffledPositions[i];
                if (maze[otherRow, otherCol] == 4) // Already has chest
                {
                    var manhattanDist = Math.Abs(row - otherRow) + Math.Abs(col - otherCol);
                    if (manhattanDist < minDistance)
                    {
                        tooClose = true;
                    }
                }
            }

            if (!tooClose)
            {
                maze[row, col] = 4; // Place chest
                placedChests++;
            }
        }
    }

    /// <summary>
    /// Counts adjacent path cells (value 2)
    /// </summary>
    private int CountAdjacentPaths(int[,] maze, int row, int col, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var count = 0;
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        foreach (var (dr, dc) in directions)
        {
            var newRow = row + dr;
            var newCol = col + dc;

            if (newRow >= 0 && newRow < mazeSize && newCol >= 0 && newCol < mazeSize &&
                maze[newRow, newCol] == 2)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Generates a classic maze using recursive backtracking starting from center
    /// </summary>
    private void GenerateClassicMazeFromCenter(int[,] maze, int centerRow, int centerCol, MazeConfig mazeConfig)
    {
        // Clear center area
        maze[centerRow, centerCol] = 0;

        // Use recursive backtracking to create maze
        CarvePassagesFrom(maze, centerRow, centerCol, mazeConfig);
    }

    /// <summary>
    /// Ensures there is a guaranteed long path from outer edge to exit
    /// Uses existing maze structure, only adds minimal connections if needed
    /// </summary>
    private void EnsurePathFromEdgeToExit(int[,] maze, int exitRow, int exitCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        // Clear 3x3 area around exit (exit will be 9 cells)
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var r = exitRow + dr;
                var c = exitCol + dc;
                if (r >= 3 && r < mazeSize - 3 && c >= 3 && c < mazeSize - 3)
                {
                    maze[r, c] = 0;
                }
            }
        }

        // Check if exit is reachable from edge through existing paths
        if (IsPositionReachableFromEdge(maze, exitRow, exitCol, mazeConfig))
        {
            return; // Already connected via existing maze paths
        }

        // If not reachable, add minimal connections using existing paths
        ConnectExitToExistingPaths(maze, exitRow, exitCol, mazeConfig);
    }

    /// <summary>
    /// Gets the length of the shortest path from edge to target position
    /// </summary>
    private int GetPathLengthFromEdge(int[,] maze, int targetRow, int targetCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var visited = new bool[mazeSize, mazeSize];
        var queue = new Queue<(int row, int col, int distance)>();

        // Start BFS from all outer edge cells that are paths (0)
        for (var i = 0; i < mazeSize; i++)
        {
            if (maze[0, i] == 0)
            {
                queue.Enqueue((0, i, 0));
                visited[0, i] = true;
            }

            if (maze[mazeSize - 1, i] == 0)
            {
                queue.Enqueue((mazeSize - 1, i, 0));
                visited[mazeSize - 1, i] = true;
            }

            if (maze[i, 0] == 0)
            {
                queue.Enqueue((i, 0, 0));
                visited[i, 0] = true;
            }

            if (maze[i, mazeSize - 1] == 0)
            {
                queue.Enqueue((i, mazeSize - 1, 0));
                visited[i, mazeSize - 1] = true;
            }
        }

        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        while (queue.Count > 0)
        {
            var (row, col, distance) = queue.Dequeue();

            if (row == targetRow && col == targetCol)
            {
                return distance;
            }

            foreach (var (dRow, dCol) in directions)
            {
                var newRow = row + dRow;
                var newCol = col + dCol;

                if (newRow >= 0 && newRow < mazeSize
                                && newCol >= 0 && newCol < mazeSize
                                && !visited[newRow, newCol]
                                && maze[newRow, newCol] == 0)
                {
                    visited[newRow, newCol] = true;
                    queue.Enqueue((newRow, newCol, distance + 1));
                }
            }
        }

        return 0; // Not reachable
    }

    /// <summary>
    /// Connects exit to existing maze paths with minimal carving
    /// Finds nearest existing path and creates short connection
    /// </summary>
    private void ConnectExitToExistingPaths(int[,] maze, int exitRow, int exitCol, MazeConfig mazeConfig)
    {
        // Find the nearest existing path cell (value 0) to the exit
        var nearestPath = FindNearestPathCell(maze, exitRow, exitCol, mazeConfig);

        if (nearestPath.HasValue)
        {
            // Create short winding connection from exit to nearest path
            CarveShortConnection(maze, exitRow, exitCol, nearestPath.Value.row, nearestPath.Value.col, mazeConfig);
        }
        else
        {
            // Fallback: if no paths found nearby, connect to edge minimally
            var (edgeRow, edgeCol) = FindNearestEdgePoint(exitRow, exitCol, mazeConfig);
            CarveShortConnection(maze, exitRow, exitCol, edgeRow, edgeCol, mazeConfig);
        }
    }

    /// <summary>
    /// Finds the nearest path cell (value 0) to the target position
    /// </summary>
    private (int row, int col)? FindNearestPathCell(int[,] maze, int targetRow, int targetCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        const int searchRadius = 30;
        var minDistance = int.MaxValue;
        (int row, int col)? nearest = null;

        for (var row = Math.Max(3, targetRow - searchRadius);
             row < Math.Min(mazeSize - 3, targetRow + searchRadius);
             row++)
        {
            for (var col = Math.Max(3, targetCol - searchRadius);
                 col < Math.Min(mazeSize - 3, targetCol + searchRadius);
                 col++)
            {
                if (maze[row, col] == 0)
                {
                    var distance = Math.Abs(row - targetRow) + Math.Abs(col - targetCol);
                    if (distance < minDistance && distance > 3) // Not too close
                    {
                        minDistance = distance;
                        nearest = (row, col);
                    }
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// Finds nearest point on edge (for fallback)
    /// </summary>
    private (int row, int col) FindNearestEdgePoint(int exitRow, int exitCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        // Determine which edge is closest
        var distToBottom = mazeSize - 1 - exitRow;
        var distToRight = mazeSize - 1 - exitCol;

        var minDist = Math.Min(Math.Min(exitRow, distToBottom), Math.Min(exitCol, distToRight));

        if (minDist == exitRow)
            return (3, exitCol);
        if (minDist == distToBottom)
            return (mazeSize - 4, exitCol);

        return minDist == exitCol ? (exitRow, 3) : (exitRow, mazeSize - 4);
    }

    /// <summary>
    /// Carves a short winding connection between two points
    /// Uses random walk to create natural-looking path
    /// </summary>
    private void CarveShortConnection(int[,] maze, int startRow, int startCol, int endRow, int endCol, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var currentRow = startRow;
        var currentCol = startCol;
        var stepCount = 0;
        var maxSteps = Math.Abs(endRow - startRow) + Math.Abs(endCol - startCol) + 20; // Allow some wandering

        while ((currentRow != endRow || currentCol != endCol) && stepCount < maxSteps)
        {
            stepCount++;

            if (currentRow >= 1 && currentRow < mazeSize - 1 && currentCol >= 1 && currentCol < mazeSize - 1)
            {
                maze[currentRow, currentCol] = 0;
            }

            // Move toward target with some randomness (70% toward, 30% random)
            if (random.Next(100) < 70)
            {
                // Move toward target
                var distRow = Math.Abs(currentRow - endRow);
                var distCol = Math.Abs(currentCol - endCol);

                if (distRow > distCol && distRow > 0)
                {
                    currentRow += Math.Sign(endRow - currentRow);
                }
                else if (distCol > 0)
                {
                    currentCol += Math.Sign(endCol - currentCol);
                }
                else if (distRow > 0)
                {
                    currentRow += Math.Sign(endRow - currentRow);
                }
            }
            else
            {
                // Random direction for slight winding
                var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
                var (dr, dc) = directions[random.Next(directions.Length)];
                currentRow += dr;
                currentCol += dc;
            }

            currentRow = Math.Max(1, Math.Min(mazeSize - 2, currentRow));
            currentCol = Math.Max(1, Math.Min(mazeSize - 2, currentCol));

            // If we hit an existing path, we're done
            if (maze[currentRow, currentCol] == 0 && (currentRow != startRow || currentCol != startCol))
            {
                break;
            }
        }
    }

    /// <summary>
    /// Checks if position is reachable from the outer edge using BFS
    /// </summary>
    private bool IsPositionReachableFromEdge(int[,] maze, int targetRow, int targetCol, MazeConfig mazeConfig)
    {
        return GetPathLengthFromEdge(maze, targetRow, targetCol, mazeConfig) > 0;
    }

    /// <summary>
    /// Flood fills all accessible paths with value 2, starting from outer edge
    /// </summary>
    private void FloodFillPaths(int[,] maze, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var queue = new Queue<(int row, int col)>();
        var visited = new System.Collections.Generic.HashSet<(int row, int col)>();

        // Start from all edges that are empty (0)
        // Top and bottom edges
        for (var col = 0; col < mazeSize; col++)
        {
            if (maze[0, col] == 0)
            {
                queue.Enqueue((0, col));
                visited.Add((0, col));
            }

            if (maze[mazeSize - 1, col] == 0)
            {
                queue.Enqueue((mazeSize - 1, col));
                visited.Add((mazeSize - 1, col));
            }
        }

        // Left and right edges
        for (var row = 1; row < mazeSize - 1; row++)
        {
            if (maze[row, 0] == 0)
            {
                queue.Enqueue((row, 0));
                visited.Add((row, 0));
            }

            if (maze[row, mazeSize - 1] == 0)
            {
                queue.Enqueue((row, mazeSize - 1));
                visited.Add((row, mazeSize - 1));
            }
        }

        // BFS to fill all accessible paths
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };

        while (queue.Count > 0)
        {
            var (row, col) = queue.Dequeue();

            // Mark as path (2), unless it's the exit (3)
            if (maze[row, col] != 3)
            {
                maze[row, col] = 2;
            }

            foreach (var (dRow, dCol) in directions)
            {
                var newRow = row + dRow;
                var newCol = col + dCol;
                var pos = (newRow, newCol);

                if (newRow >= 0 && newRow < mazeSize &&
                    newCol >= 0 && newCol < mazeSize &&
                    !visited.Contains(pos) &&
                    (maze[newRow, newCol] == 0 || maze[newRow, newCol] == 3))
                {
                    visited.Add(pos);
                    queue.Enqueue(pos);
                }
            }
        }
    }

    /// <summary>
    /// Generates a maze with guaranteed solution path.
    /// Returns both the maze and the solution path coordinates.
    /// </summary>
    public (int[,] maze, List<(int row, int col)> solution) GenerateMazeWithSolution(MazeConfig mazeConfig)
    {
        var (maze, exitPos) = GenerateMaze(mazeConfig);

        if (exitPos == (-1, -1))
        {
            return (maze, []);
        }

        var startPos = GetRandomStartPosition(maze, mazeConfig);
        var solution = FindPath(maze, startPos, exitPos, mazeConfig);

        return (maze, solution);
    }

    /// <summary>
    /// Finds the exit position in the maze (cell with value 3)
    /// </summary>
    private (int row, int col)? FindExitPosition(int[,] maze, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        for (var row = 0; row < mazeSize; row++)
        {
            for (var col = 0; col < mazeSize; col++)
            {
                if (maze[row, col] == 3)
                {
                    return (row, col);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets a random starting position on the outer edges where there is a path (value 2).
    /// </summary>
    public (int row, int col) GetRandomStartPosition(int[,] maze, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var validPositions = new List<(int row, int col)>();

        // Collect all valid path positions (value 2) on outer edges
        // Top and bottom edges
        for (var col = 0; col < mazeSize; col++)
        {
            if (maze[0, col] == 2) validPositions.Add((0, col));
            if (maze[mazeSize - 1, col] == 2) validPositions.Add((mazeSize - 1, col));
        }

        // Left and right edges
        for (var row = 1; row < mazeSize - 1; row++)
        {
            if (maze[row, 0] == 2) validPositions.Add((row, 0));
            if (maze[row, mazeSize - 1] == 2) validPositions.Add((row, mazeSize - 1));
        }

        // Return random position from valid ones
        return validPositions.Count > 0
            ? validPositions[random.Next(validPositions.Count)]
            : (0, 0); // Fallback
    }

    /// <summary>
    /// Finds a path from start to end using BFS algorithm.
    /// Works with new maze values: 0=empty, 1=wall, 2=path, 3=exit
    /// </summary>
    private List<(int row, int col)> FindPath(int[,] maze, (int row, int col) start, (int row, int col) end, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        var queue = new Queue<(int row, int col, List<(int row, int col)> path)>();
        var visited = new System.Collections.Generic.HashSet<(int row, int col)>();

        queue.Enqueue((start.row, start.col, new List<(int row, int col)> { start }));
        visited.Add(start);

        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        while (queue.Count > 0)
        {
            var (row, col, path) = queue.Dequeue();

            if (row == end.row && col == end.col)
            {
                return path;
            }

            foreach (var (dRow, dCol) in directions)
            {
                var newRow = row + dRow;
                var newCol = col + dCol;
                var newPos = (newRow, newCol);

                if (newRow >= 0 && newRow < mazeSize
                                && newCol >= 0
                                && newCol < mazeSize
                                && !visited.Contains(newPos)
                                && (maze[newRow, newCol] == 2 || maze[newRow, newCol] == 3 ||
                                    maze[newRow, newCol] == 4)) // Can walk on paths (2), exit (3), and chests (4)
                {
                    visited.Add(newPos);
                    var newPath = new List<(int row, int col)>(path) { newPos };
                    queue.Enqueue((newRow, newCol, newPath));
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Recursive backtracking to carve passages - creates long connected corridors
    /// </summary>
    private void CarvePassagesFrom(int[,] maze, int row, int col, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        // Directions: up, right, down, left
        var directions = new[] { (-1, 0), (0, 1), (1, 0), (0, -1) };

        // Shuffle directions for randomness
        for (var i = directions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }

        foreach (var (dRow, dCol) in directions)
        {
            // Look 2 cells ahead (to leave walls between corridors)
            var newRow = row + dRow * 2;
            var newCol = col + dCol * 2;

            // Check bounds
            if (newRow < 1 || newRow >= mazeSize - 1 || newCol < 1 || newCol >= mazeSize - 1)
                continue;

            // If the target cell is a wall, carve a path to it
            if (maze[newRow, newCol] == 1)
            {
                // Carve the cell between
                maze[row + dRow, col + dCol] = 0;
                // Carve the target cell
                maze[newRow, newCol] = 0;

                // Recurse from the new cell
                CarvePassagesFrom(maze, newRow, newCol, mazeConfig);
            }
        }
    }

    /// <summary>
    /// Clears the outer edge of the maze (2-3 cells deep) to create a clear starting area
    /// </summary>
    private void ClearOuterEdge(int[,] maze, MazeConfig mazeConfig)
    {
        var mazeSize = mazeConfig.MazeSize;
        const int edgeWidth = 3;

        for (var row = 0; row < mazeSize; row++)
        {
            for (var col = 0; col < mazeSize; col++)
            {
                // Check if cell is within edge distance from any border
                if (row < edgeWidth || row >= mazeSize - edgeWidth ||
                    col < edgeWidth || col >= mazeSize - edgeWidth)
                {
                    maze[row, col] = 0;
                }
            }
        }
    }
}
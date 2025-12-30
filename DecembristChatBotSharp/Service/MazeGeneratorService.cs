using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MazeGeneratorService(Random random)
{
    private const int MazeSize = 128;

    /// <summary>
    /// Generates a 128x128 maze where 0 is empty space, 1 is a wall, 2 is a path, 3 is the exit, and 4 is a chest.
    /// Classic maze with one solution path from edge to exit.
    /// Exit is placed randomly (not at center, not at edge) with guaranteed long path.
    /// Chests are placed randomly throughout the maze (1 per ~10 cells) without blocking paths.
    /// Outer edge is clear for starting area.
    /// </summary>
    public int[,] GenerateMaze()
    {
        var maze = new int[MazeSize, MazeSize];
        
        // Initialize all cells as walls (1)
        for (var i = 0; i < MazeSize; i++)
        {
            for (var j = 0; j < MazeSize; j++)
            {
                maze[i, j] = 1;
            }
        }

        var centerRow = MazeSize / 2;
        var centerCol = MazeSize / 2;
        
        // Step 1: Clear the outer edge (starting area)
        ClearOuterEdge(maze);
        
        // Step 2: Generate classic maze using recursive backtracking from center
        GenerateClassicMazeFromCenter(maze, centerRow, centerCol);
        
        // Step 3: Choose random exit position (not too close to edge, not at center)
        var (exitRow, exitCol) = ChooseRandomExitPosition(centerRow, centerCol);
        
        // Step 4: Ensure there's a long path from edge to exit
        EnsurePathFromEdgeToExit(maze, exitRow, exitCol);
        
        // Step 5: Mark exit
        maze[exitRow, exitCol] = 3;
        
        // Step 6: Fill all accessible paths with value 2 (will preserve 3 for exit)
        FloodFillPaths(maze);
        
        // Step 7: Place chests randomly (1 per ~10 cells, value 4)
        PlaceChestsInMaze(maze);

        return maze;
    }

    /// <summary>
    /// Chooses a random exit position that is:
    /// - Not too close to edge (at least 40 cells away)
    /// - Not at the exact center
    /// - In a position that allows for a long path
    /// </summary>
    private (int row, int col) ChooseRandomExitPosition(int centerRow, int centerCol)
    {
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
                exitCol = random.Next(centerCol + 20, MazeSize - minDistanceFromEdge);
                break;
            case 2: // Bottom-right quadrant
                exitRow = random.Next(centerRow + 20, MazeSize - minDistanceFromEdge);
                exitCol = random.Next(centerCol + 20, MazeSize - minDistanceFromEdge);
                break;
            default: // Bottom-left quadrant
                exitRow = random.Next(centerRow + 20, MazeSize - minDistanceFromEdge);
                exitCol = random.Next(minDistanceFromEdge, centerCol - 20);
                break;
        }
        
        return (exitRow, exitCol);
    }

    /// <summary>
    /// Places chests randomly in the maze (value 4)
    /// Approximately 1 chest per 100 path cells (very rare)
    /// Chests are placed on paths but don't block them
    /// </summary>
    private void PlaceChestsInMaze(int[,] maze)
    {
        // Count total path cells (value 2)
        var pathCellCount = 0;
        for (var i = 0; i < MazeSize; i++)
        {
            for (var j = 0; j < MazeSize; j++)
            {
                if (maze[i, j] == 2)
                    pathCellCount++;
            }
        }
        
        // Calculate number of chests (1 per 100 path cells - very rare)
        var chestCount = pathCellCount / 100;
        
        // Collect all valid path positions
        var validPathPositions = new List<(int row, int col)>();
        for (var i = 0; i < MazeSize; i++)
        {
            for (var j = 0; j < MazeSize; j++)
            {
                if (maze[i, j] == 2)
                {
                    // Check if this is not a critical path cell (has multiple exits)
                    // A cell is safe for chest if it has at least 2 adjacent path cells
                    var adjacentPaths = CountAdjacentPaths(maze, i, j);
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
            
            // Ensure chests are not too close to each other (at least 15 cells apart)
            var tooClose = false;
            for (var dr = -15; dr <= 15; dr++)
            {
                for (var dc = -15; dc <= 15; dc++)
                {
                    var checkRow = row + dr;
                    var checkCol = col + dc;
                    if (checkRow is >= 0 and < MazeSize && checkCol is >= 0 and < MazeSize && maze[checkRow, checkCol] == 4)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) break;
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
    private int CountAdjacentPaths(int[,] maze, int row, int col)
    {
        var count = 0;
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
        
        foreach (var (dr, dc) in directions)
        {
            var newRow = row + dr;
            var newCol = col + dc;
            
            if (newRow is >= 0 and < MazeSize && newCol is >= 0 and < MazeSize &&
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
    private void GenerateClassicMazeFromCenter(int[,] maze, int centerRow, int centerCol)
    {
        // Clear center area
        maze[centerRow, centerCol] = 0;
        
        // Use recursive backtracking to create maze
        CarvePassagesFrom(maze, centerRow, centerCol);
    }

    /// <summary>
    /// Ensures there is a guaranteed long path from outer edge to exit
    /// Uses existing maze structure, only adds minimal connections if needed
    /// </summary>
    private void EnsurePathFromEdgeToExit(int[,] maze, int exitRow, int exitCol)
    {
        // Clear small area around exit to ensure it's accessible
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var r = exitRow + dr;
                var c = exitCol + dc;
                if (r is >= 3 and < MazeSize - 3 && c is >= 3 and < MazeSize - 3)
                {
                    maze[r, c] = 0;
                }
            }
        }
        
        // Check if exit is reachable from edge through existing paths
        if (IsPositionReachableFromEdge(maze, exitRow, exitCol))
        {
            return; // Already connected via existing maze paths
        }
        
        // If not reachable, add minimal connections using existing paths
        ConnectExitToExistingPaths(maze, exitRow, exitCol);
    }

    /// <summary>
    /// Gets the length of the shortest path from edge to target position
    /// </summary>
    private int GetPathLengthFromEdge(int[,] maze, int targetRow, int targetCol)
    {
        var visited = new bool[MazeSize, MazeSize];
        var queue = new Queue<(int row, int col, int distance)>();
        
        // Start BFS from all outer edge cells that are paths (0)
        for (var i = 0; i < MazeSize; i++)
        {
            if (maze[0, i] == 0)
            {
                queue.Enqueue((0, i, 0));
                visited[0, i] = true;
            }
            if (maze[MazeSize - 1, i] == 0)
            {
                queue.Enqueue((MazeSize - 1, i, 0));
                visited[MazeSize - 1, i] = true;
            }
            if (maze[i, 0] == 0)
            {
                queue.Enqueue((i, 0, 0));
                visited[i, 0] = true;
            }
            if (maze[i, MazeSize - 1] == 0)
            {
                queue.Enqueue((i, MazeSize - 1, 0));
                visited[i, MazeSize - 1] = true;
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
                
                if (newRow is >= 0 and < MazeSize && newCol is >= 0 and < MazeSize &&
                    !visited[newRow, newCol] && maze[newRow, newCol] == 0)
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
    private void ConnectExitToExistingPaths(int[,] maze, int exitRow, int exitCol)
    {
        // Find the nearest existing path cell (value 0) to the exit
        var nearestPath = FindNearestPathCell(maze, exitRow, exitCol);
        
        if (nearestPath.HasValue)
        {
            // Create short winding connection from exit to nearest path
            CarveShortConnection(maze, exitRow, exitCol, nearestPath.Value.row, nearestPath.Value.col);
        }
        else
        {
            // Fallback: if no paths found nearby, connect to edge minimally
            var (edgeRow, edgeCol) = FindNearestEdgePoint(exitRow, exitCol);
            CarveShortConnection(maze, exitRow, exitCol, edgeRow, edgeCol);
        }
    }

    /// <summary>
    /// Finds the nearest path cell (value 0) to the target position
    /// </summary>
    private (int row, int col)? FindNearestPathCell(int[,] maze, int targetRow, int targetCol)
    {
        var searchRadius = 30;
        var minDistance = int.MaxValue;
        (int row, int col)? nearest = null;
        
        for (var row = Math.Max(3, targetRow - searchRadius); row < Math.Min(MazeSize - 3, targetRow + searchRadius); row++)
        {
            for (var col = Math.Max(3, targetCol - searchRadius); col < Math.Min(MazeSize - 3, targetCol + searchRadius); col++)
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
    private (int row, int col) FindNearestEdgePoint(int exitRow, int exitCol)
    {
        // Determine which edge is closest
        var distToTop = exitRow;
        var distToBottom = MazeSize - 1 - exitRow;
        var distToLeft = exitCol;
        var distToRight = MazeSize - 1 - exitCol;
        
        var minDist = Math.Min(Math.Min(distToTop, distToBottom), Math.Min(distToLeft, distToRight));
        
        if (minDist == distToTop)
            return (3, exitCol);
        if (minDist == distToBottom)
            return (MazeSize - 4, exitCol);
        
        return minDist == distToLeft ? (exitRow, 3) : (exitRow, MazeSize - 4);
    }

    /// <summary>
    /// Carves a short winding connection between two points
    /// Uses random walk to create natural-looking path
    /// </summary>
    private void CarveShortConnection(int[,] maze, int startRow, int startCol, int endRow, int endCol)
    {
        var currentRow = startRow;
        var currentCol = startCol;
        var stepCount = 0;
        var maxSteps = Math.Abs(endRow - startRow) + Math.Abs(endCol - startCol) + 20; // Allow some wandering
        
        while ((currentRow != endRow || currentCol != endCol) && stepCount < maxSteps)
        {
            stepCount++;
            
            if (currentRow is >= 1 and < MazeSize - 1 && currentCol is >= 1 and < MazeSize - 1)
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
            
            currentRow = Math.Max(1, Math.Min(MazeSize - 2, currentRow));
            currentCol = Math.Max(1, Math.Min(MazeSize - 2, currentCol));
            
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
    private bool IsPositionReachableFromEdge(int[,] maze, int targetRow, int targetCol)
    {
        return GetPathLengthFromEdge(maze, targetRow, targetCol) > 0;
    }

    /// <summary>
    /// Generates maze using small reusable chunks (mini-mazes)
    /// </summary>
    private void GenerateMazeFromChunks(int[,] maze, int centerRow, int centerCol)
    {
        const int chunkSize = 8; // Size of each chunk
        const int chunksPerRow = MazeSize / chunkSize;
        
        // Generate chunks across the entire maze
        for (var chunkRow = 0; chunkRow < chunksPerRow; chunkRow++)
        {
            for (var chunkCol = 0; chunkCol < chunksPerRow; chunkCol++)
            {
                var startRow = chunkRow * chunkSize;
                var startCol = chunkCol * chunkSize;
                
                // Skip edge chunks (already cleared)
                if (startRow < 3 || startRow >= MazeSize - 3 - chunkSize ||
                    startCol < 3 || startCol >= MazeSize - 3 - chunkSize)
                {
                    continue;
                }
                
                // Generate a random chunk pattern
                GenerateRandomChunk(maze, startRow, startCol, chunkSize);
            }
        }
        
        // Connect chunks together
        ConnectChunks(maze, chunkSize);
    }

    /// <summary>
    /// Generates a random mini-maze chunk at the specified position
    /// </summary>
    private void GenerateRandomChunk(int[,] maze, int startRow, int startCol, int chunkSize)
    {
        // Pick a random chunk pattern type
        var patternType = random.Next(8);
        
        switch (patternType)
        {
            case 0:
                GenerateSpiralChunk(maze, startRow, startCol, chunkSize);
                break;
            case 1:
                GenerateCrossChunk(maze, startRow, startCol, chunkSize);
                break;
            case 2:
                GenerateZigzagChunk(maze, startRow, startCol, chunkSize);
                break;
            case 3:
                GenerateBranchingChunk(maze, startRow, startCol, chunkSize);
                break;
            case 4:
                GenerateRoomsChunk(maze, startRow, startCol, chunkSize);
                break;
            case 5:
                GenerateMazeRecursiveChunk(maze, startRow, startCol, chunkSize);
                break;
            case 6:
                GenerateCorridorChunk(maze, startRow, startCol, chunkSize);
                break;
            default:
                GenerateDenseChunk(maze, startRow, startCol, chunkSize);
                break;
        }
    }

    /// <summary>
    /// Spiral pattern chunk
    /// </summary>
    private void GenerateSpiralChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var layer = 0;
        while (layer < size / 2)
        {
            // Top
            for (var col = layer; col < size - layer; col++)
            {
                if (startRow + layer < MazeSize && startCol + col < MazeSize)
                    maze[startRow + layer, startCol + col] = 0;
            }
            // Right
            for (var row = layer + 1; row < size - layer; row++)
            {
                if (startRow + row < MazeSize && startCol + size - layer - 1 < MazeSize)
                    maze[startRow + row, startCol + size - layer - 1] = 0;
            }
            // Bottom
            if (size - layer - 1 > layer)
            {
                for (var col = size - layer - 2; col >= layer; col--)
                {
                    if (startRow + size - layer - 1 < MazeSize && startCol + col < MazeSize)
                        maze[startRow + size - layer - 1, startCol + col] = 0;
                }
            }
            // Left
            if (size - layer - 1 > layer + 1)
            {
                for (var row = size - layer - 2; row > layer; row--)
                {
                    if (startRow + row < MazeSize && startCol + layer < MazeSize)
                        maze[startRow + row, startCol + layer] = 0;
                }
            }
            layer++;
        }
    }

    /// <summary>
    /// Cross pattern chunk
    /// </summary>
    private void GenerateCrossChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var mid = size / 2;
        // Horizontal line
        for (var col = 0; col < size; col++)
        {
            if (startRow + mid < MazeSize && startCol + col < MazeSize)
                maze[startRow + mid, startCol + col] = 0;
        }
        // Vertical line
        for (var row = 0; row < size; row++)
        {
            if (startRow + row < MazeSize && startCol + mid < MazeSize)
                maze[startRow + row, startCol + mid] = 0;
        }
    }

    /// <summary>
    /// Zigzag pattern chunk
    /// </summary>
    private void GenerateZigzagChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var direction = random.Next(2); // 0 = horizontal zigzag, 1 = vertical zigzag
        
        if (direction == 0)
        {
            for (var row = 0; row < size; row++)
            {
                var colStart = (row % 2 == 0) ? 0 : size - 1;
                var colEnd = (row % 2 == 0) ? size : -1;
                var colStep = (row % 2 == 0) ? 1 : -1;
                
                for (var col = colStart; col != colEnd; col += colStep)
                {
                    if (startRow + row < MazeSize && startCol + col < MazeSize)
                        maze[startRow + row, startCol + col] = 0;
                }
            }
        }
        else
        {
            for (var col = 0; col < size; col++)
            {
                var rowStart = (col % 2 == 0) ? 0 : size - 1;
                var rowEnd = (col % 2 == 0) ? size : -1;
                var rowStep = (col % 2 == 0) ? 1 : -1;
                
                for (var row = rowStart; row != rowEnd; row += rowStep)
                {
                    if (startRow + row < MazeSize && startCol + col < MazeSize)
                        maze[startRow + row, startCol + col] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Branching pattern chunk (tree-like)
    /// </summary>
    private void GenerateBranchingChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var mid = size / 2;
        // Main trunk
        for (var row = 0; row < size; row++)
        {
            if (startRow + row < MazeSize && startCol + mid < MazeSize)
                maze[startRow + row, startCol + mid] = 0;
        }
        // Branches
        for (var i = 1; i < size; i += 2)
        {
            for (var j = 0; j <= mid; j++)
            {
                if (startRow + i < MazeSize && startCol + mid - j < MazeSize)
                    maze[startRow + i, startCol + mid - j] = 0;
                if (startRow + i < MazeSize && startCol + mid + j < MazeSize)
                    maze[startRow + i, startCol + mid + j] = 0;
            }
        }
    }

    /// <summary>
    /// Rooms pattern chunk (2x2 rooms connected)
    /// </summary>
    private void GenerateRoomsChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var roomSize = size / 2;
        
        // Create 4 small rooms
        for (var bigRow = 0; bigRow < 2; bigRow++)
        {
            for (var bigCol = 0; bigCol < 2; bigCol++)
            {
                var roomStartRow = bigRow * roomSize;
                var roomStartCol = bigCol * roomSize;
                
                // Fill room
                for (var r = roomStartRow; r < roomStartRow + roomSize; r++)
                {
                    for (var c = roomStartCol; c < roomStartCol + roomSize; c++)
                    {
                        if (startRow + r < MazeSize && startCol + c < MazeSize)
                            maze[startRow + r, startCol + c] = 0;
                    }
                }
            }
        }
        
        // Add walls between rooms
        for (var i = 1; i < size - 1; i++)
        {
            if (i != roomSize && random.Next(2) == 0)
            {
                if (startRow + roomSize < MazeSize && startCol + i < MazeSize)
                    maze[startRow + roomSize, startCol + i] = 1; // Horizontal wall
            }
            if (i != roomSize && random.Next(2) == 0)
            {
                if (startRow + i < MazeSize && startCol + roomSize < MazeSize)
                    maze[startRow + i, startCol + roomSize] = 1; // Vertical wall
            }
        }
    }

    /// <summary>
    /// Recursive backtracking mini-maze chunk
    /// </summary>
    private void GenerateMazeRecursiveChunk(int[,] maze, int startRow, int startCol, int size)
    {
        // Create a mini recursive maze within the chunk
        var visited = new bool[size, size];
        CarveChunkPath(maze, startRow, startCol, size / 2, size / 2, visited, size);
    }

    private void CarveChunkPath(int[,] maze, int startRow, int startCol, int row, int col, bool[,] visited, int size)
    {
        if (row < 0 || row >= size || col < 0 || col >= size || visited[row, col])
            return;
        
        visited[row, col] = true;
        if (startRow + row < MazeSize && startCol + col < MazeSize)
            maze[startRow + row, startCol + col] = 0;
        
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        // Shuffle
        for (var i = directions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }
        
        foreach (var (dr, dc) in directions)
        {
            if (random.Next(100) < 70) // 70% chance to continue
            {
                CarveChunkPath(maze, startRow, startCol, row + dr, col + dc, visited, size);
            }
        }
    }

    /// <summary>
    /// Corridor pattern chunk (straight corridors)
    /// </summary>
    private void GenerateCorridorChunk(int[,] maze, int startRow, int startCol, int size)
    {
        var isHorizontal = random.Next(2) == 0;
        var numCorridors = random.Next(2, 4);
        
        for (var i = 0; i < numCorridors; i++)
        {
            var pos = (i + 1) * size / (numCorridors + 1);
            
            if (isHorizontal)
            {
                for (var col = 0; col < size; col++)
                {
                    if (startRow + pos < MazeSize && startCol + col < MazeSize)
                        maze[startRow + pos, startCol + col] = 0;
                }
            }
            else
            {
                for (var row = 0; row < size; row++)
                {
                    if (startRow + row < MazeSize && startCol + pos < MazeSize)
                        maze[startRow + row, startCol + pos] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Dense maze pattern chunk
    /// </summary>
    private void GenerateDenseChunk(int[,] maze, int startRow, int startCol, int size)
    {
        // Create a dense grid pattern
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                if ((row % 2 == 0 || col % 2 == 0) && random.Next(100) < 60)
                {
                    if (startRow + row < MazeSize && startCol + col < MazeSize)
                        maze[startRow + row, startCol + col] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Connects chunks together by ensuring paths between them
    /// </summary>
    private void ConnectChunks(int[,] maze, int chunkSize)
    {
        // For each chunk boundary, create random connections
        for (var row = chunkSize; row < MazeSize - chunkSize; row += chunkSize)
        {
            for (var col = 3; col < MazeSize - 3; col++)
            {
                // Create horizontal connections (between vertically adjacent chunks)
                if (random.Next(100) < 40) // 40% chance
                {
                    maze[row, col] = 0;
                    if (row > 0) maze[row - 1, col] = 0;
                    if (row < MazeSize - 1) maze[row + 1, col] = 0;
                }
            }
        }
        
        for (var col = chunkSize; col < MazeSize - chunkSize; col += chunkSize)
        {
            for (var row = 3; row < MazeSize - 3; row++)
            {
                // Create vertical connections (between horizontally adjacent chunks)
                if (random.Next(100) < 40) // 40% chance
                {
                    maze[row, col] = 0;
                    if (col > 0) maze[row, col - 1] = 0;
                    if (col < MazeSize - 1) maze[row, col + 1] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Builds multi-path maze with chunks - uses existing chunk structure to create correct/incorrect paths
    /// </summary>
    private void BuildMultiPathMazeWithChunks(int[,] maze, int centerRow, int centerCol, 
        (int distance, int wallThickness, int totalOpenings, int correctOpenings)[] rings)
    {
        // PHASE 1: Build ring walls preserving chunk paths
        for (var i = rings.Length - 1; i >= 0; i--)
        {
            var ring = rings[i];
            CreateSquareRingPreservingPaths(maze, centerRow, centerCol, ring.distance, ring.wallThickness);
        }
        
        // PHASE 2: Create all openings in all rings
        var allRingOpenings = new List<(int row, int col, int side)>[rings.Length];
        for (var ringIndex = 0; ringIndex < rings.Length; ringIndex++)
        {
            var ring = rings[ringIndex];
            allRingOpenings[ringIndex] = CreateRingOpeningsWithTracking(maze, centerRow, centerCol, 
                ring.distance, ring.wallThickness, ring.totalOpenings);
        }
        
        // PHASE 3: Build correct paths using chunk-aware pathfinding
        var correctOpeningsPerRing = new List<int>[rings.Length];
        
        // For innermost ring, select which openings lead to center
        var innerRing = rings[0];
        var innerCorrectIndices = Enumerable.Range(0, allRingOpenings[0].Count).ToList();
        Shuffle(innerCorrectIndices);
        correctOpeningsPerRing[0] = innerCorrectIndices.Take(innerRing.correctOpenings).ToList();
        
        // Connect innermost correct openings to center using existing paths
        foreach (var openingIdx in correctOpeningsPerRing[0])
        {
            var opening = allRingOpenings[0][openingIdx];
            CarveChunkAwarePath(maze, opening.row, opening.col, centerRow, centerCol);
        }
        
        // For each outer ring, connect to inner ring using chunk-aware pathfinding
        for (var ringIndex = 1; ringIndex < rings.Length; ringIndex++)
        {
            var ring = rings[ringIndex];
            var innerRingIndex = ringIndex - 1;
            
            // Select which openings in this ring are "correct"
            var availableIndices = Enumerable.Range(0, allRingOpenings[ringIndex].Count).ToList();
            Shuffle(availableIndices);
            correctOpeningsPerRing[ringIndex] = availableIndices.Take(ring.correctOpenings).ToList();
            
            // Connect each correct opening to a random correct opening in the inner ring
            foreach (var outerOpeningIdx in correctOpeningsPerRing[ringIndex])
            {
                var outerOpening = allRingOpenings[ringIndex][outerOpeningIdx];
                
                // Pick a random correct opening from inner ring
                var innerCorrectOpenings = correctOpeningsPerRing[innerRingIndex];
                if (innerCorrectOpenings.Count == 0) continue;
                
                var targetInnerIdx = innerCorrectOpenings[random.Next(innerCorrectOpenings.Count)];
                var innerOpening = allRingOpenings[innerRingIndex][targetInnerIdx];
                
                // Carve path using existing chunk structure
                CarveChunkAwarePath(maze, outerOpening.row, outerOpening.col, innerOpening.row, innerOpening.col);
            }
        }
        
        // PHASE 4: Create dead-end paths for incorrect openings
        for (var ringIndex = 0; ringIndex < rings.Length; ringIndex++)
        {
            var ring = rings[ringIndex];
            var correctIndices = new System.Collections.Generic.HashSet<int>(correctOpeningsPerRing[ringIndex]);
            
            for (var openingIdx = 0; openingIdx < allRingOpenings[ringIndex].Count; openingIdx++)
            {
                if (correctIndices.Contains(openingIdx)) continue;
                
                var opening = allRingOpenings[ringIndex][openingIdx];
                var innerDistance = ringIndex > 0 ? rings[ringIndex - 1].distance : 0;
                CarveDeadEndPath(maze, opening.row, opening.col, centerRow, centerCol, ring.distance, innerDistance);
            }
        }
    }

    /// <summary>
    /// Carves path between two points using existing chunk structure (preferring existing paths)
    /// </summary>
    private void CarveChunkAwarePath(int[,] maze, int startRow, int startCol, int endRow, int endCol)
    {
        // Use A* pathfinding that prefers existing empty cells
        var path = FindPathPreferringExisting(maze, startRow, startCol, endRow, endCol);
        
        if (path != null && path.Count > 0)
        {
            // Carve the path
            foreach (var (row, col) in path)
            {
                if (row >= 0 && row < MazeSize && col >= 0 && col < MazeSize && maze[row, col] != 3)
                {
                    maze[row, col] = 0;
                }
            }
        }
        else
        {
            // Fallback: carve direct path if no path found
            CarveDirectPath(maze, startRow, startCol, endRow, endCol);
        }
    }

    /// <summary>
    /// Finds path preferring existing empty cells (chunk paths)
    /// </summary>
    private List<(int row, int col)> FindPathPreferringExisting(int[,] maze, int startRow, int startCol, int endRow, int endCol)
    {
        var openSet = new PriorityQueue<(int row, int col, int gCost, int fCost, List<(int row, int col)> path)>();
        var visited = new System.Collections.Generic.HashSet<(int row, int col)>();
        
        openSet.Enqueue((startRow, startCol, 0, 0, new List<(int row, int col)> { (startRow, startCol) }), 0);
        
        var directions = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        var maxSteps = 500;
        var stepCount = 0;
        
        while (openSet.Count > 0 && stepCount < maxSteps)
        {
            stepCount++;
            var (row, col, gCost, _, path) = openSet.Dequeue();
            
            if (row == endRow && col == endCol)
            {
                return path;
            }
            
            if (visited.Contains((row, col)))
                continue;
            
            visited.Add((row, col));
            
            foreach (var (dr, dc) in directions)
            {
                var newRow = row + dr;
                var newCol = col + dc;
                
                if (newRow < 0 || newRow >= MazeSize || newCol < 0 || newCol >= MazeSize)
                    continue;
                
                if (visited.Contains((newRow, newCol)))
                    continue;
                
                // Cost calculation: prefer existing empty cells
                var moveCost = maze[newRow, newCol] == 0 ? 1 : 10; // Existing path is cheaper
                var newGCost = gCost + moveCost;
                var hCost = Math.Abs(endRow - newRow) + Math.Abs(endCol - newCol);
                var newFCost = newGCost + hCost;
                
                var newPath = new List<(int row, int col)>(path) { (newRow, newCol) };
                openSet.Enqueue((newRow, newCol, newGCost, newFCost, newPath), newFCost);
            }
        }
        
        return null; // No path found
    }

    /// <summary>
    /// Simple priority queue implementation
    /// </summary>
    private class PriorityQueue<T> where T : notnull
    {
        private readonly List<(T item, int priority)> items = new List<(T, int)>();
        
        public int Count => items.Count;
        
        public void Enqueue(T item, int priority)
        {
            items.Add((item, priority));
        }
        
        public T Dequeue()
        {
            var minIndex = 0;
            for (var i = 1; i < items.Count; i++)
            {
                if (items[i].priority < items[minIndex].priority)
                    minIndex = i;
            }
            
            var item = items[minIndex].item;
            items.RemoveAt(minIndex);
            return item;
        }
    }

    /// <summary>
    /// Carves a direct path as fallback
    /// </summary>
    private void CarveDirectPath(int[,] maze, int startRow, int startCol, int endRow, int endCol)
    {
        var currentRow = startRow;
        var currentCol = startCol;
        
        while (currentRow != endRow || currentCol != endCol)
        {
            if (currentRow >= 0 && currentRow < MazeSize && currentCol >= 0 && currentCol < MazeSize && maze[currentRow, currentCol] != 3)
            {
                maze[currentRow, currentCol] = 0;
            }
            
            if (currentRow != endRow)
                currentRow += Math.Sign(endRow - currentRow);
            else if (currentCol != endCol)
                currentCol += Math.Sign(endCol - currentCol);
        }
        
        if (endRow >= 0 && endRow < MazeSize && endCol >= 0 && endCol < MazeSize && maze[endRow, endCol] != 3)
        {
            maze[endRow, endCol] = 0;
        }
    }

    /// <summary>
    /// Creates ring openings and returns their positions
    /// </summary>
    private List<(int row, int col, int side)> CreateRingOpeningsWithTracking(int[,] maze, int centerRow, int centerCol, 
        int distance, int thickness, int openingCount)
    {
        var openings = new List<(int row, int col, int side)>();
        
        // Sides: 0=top, 1=right, 2=bottom, 3=left
        var attempts = 0;
        var maxAttempts = openingCount * 50;
        
        while (openings.Count < openingCount && attempts < maxAttempts)
        {
            attempts++;
            
            var side = random.Next(4);
            var offset = random.Next(-distance + thickness * 2, distance - thickness * 2 + 1);
            
            int row, col;
            switch (side)
            {
                case 0: // Top
                    row = centerRow - distance;
                    col = centerCol + offset;
                    break;
                case 1: // Right
                    row = centerRow + offset;
                    col = centerCol + distance;
                    break;
                case 2: // Bottom
                    row = centerRow + distance;
                    col = centerCol + offset;
                    break;
                default: // Left
                    row = centerRow + offset;
                    col = centerCol - distance;
                    break;
            }
            
            // Check bounds
            if (row < 0 || row >= MazeSize || col < 0 || col >= MazeSize)
                continue;
            
            // Check if too close to another opening
            var tooClose = false;
            foreach (var (oRow, oCol, _) in openings)
            {
                if (Math.Abs(oRow - row) + Math.Abs(oCol - col) < thickness * 4)
                {
                    tooClose = true;
                    break;
                }
            }
            
            if (!tooClose)
            {
                openings.Add((row, col, side));
                
                // Carve the opening through the wall
                for (var t = 0; t < thickness; t++)
                {
                    int carveRow = row, carveCol = col;
                    
                    switch (side)
                    {
                        case 0: // Top - carve downward
                            carveRow = centerRow - distance + t;
                            break;
                        case 1: // Right - carve leftward  
                            carveCol = centerCol + distance - t;
                            break;
                        case 2: // Bottom - carve upward
                            carveRow = centerRow + distance - t;
                            break;
                        case 3: // Left - carve rightward
                            carveCol = centerCol - distance + t;
                            break;
                    }
                    
                    if (carveRow >= 0 && carveRow < MazeSize && carveCol >= 0 && carveCol < MazeSize)
                    {
                        maze[carveRow, carveCol] = 0;
                    }
                }
            }
        }
        
        return openings;
    }


    /// <summary>
    /// Creates a square ring that preserves existing paths (doesn't overwrite value 0)
    /// </summary>
    private void CreateSquareRingPreservingPaths(int[,] maze, int centerRow, int centerCol, int distance, int thickness)
    {
        for (var t = 0; t < thickness; t++)
        {
            var currentDist = distance + t;
            
            for (var offset = -currentDist; offset <= currentDist; offset++)
            {
                // Top edge
                if (centerRow - currentDist >= 0 && centerCol + offset >= 0 && centerCol + offset < MazeSize)
                {
                    if (maze[centerRow - currentDist, centerCol + offset] != 0)
                        maze[centerRow - currentDist, centerCol + offset] = 1;
                }
                
                // Bottom edge
                if (centerRow + currentDist < MazeSize && centerCol + offset >= 0 && centerCol + offset < MazeSize)
                {
                    if (maze[centerRow + currentDist, centerCol + offset] != 0)
                        maze[centerRow + currentDist, centerCol + offset] = 1;
                }
                
                // Left edge
                if (centerCol - currentDist >= 0 && centerRow + offset >= 0 && centerRow + offset < MazeSize)
                {
                    if (maze[centerRow + offset, centerCol - currentDist] != 0)
                        maze[centerRow + offset, centerCol - currentDist] = 1;
                }
                
                // Right edge
                if (centerCol + currentDist < MazeSize && centerRow + offset >= 0 && centerRow + offset < MazeSize)
                {
                    if (maze[centerRow + offset, centerCol + currentDist] != 0)
                        maze[centerRow + offset, centerCol + currentDist] = 1;
                }
            }
        }
    }

    /// <summary>
    /// Shuffles a list in place
    /// </summary>
    private void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Carves a winding path from opening to center
    /// </summary>
    private void CarveWindingPathToCenter(int[,] maze, int startRow, int startCol, int centerRow, int centerCol, int ringDistance)
    {
        var currentRow = startRow;
        var currentCol = startCol;
        
        // Move inward from ring opening
        for (var i = 0; i < 3; i++)
        {
            maze[currentRow, currentCol] = 0;
            var dRow = Math.Sign(centerRow - currentRow);
            var dCol = Math.Sign(centerCol - currentCol);
            
            if (Math.Abs(dRow) > 0) currentRow += dRow;
            else if (Math.Abs(dCol) > 0) currentCol += dCol;
            
            currentRow = Math.Max(0, Math.Min(MazeSize - 1, currentRow));
            currentCol = Math.Max(0, Math.Min(MazeSize - 1, currentCol));
        }
        
        // Now create a winding path to center using random walk with bias toward center
        var stepCount = 0;
        var maxSteps = 1000;
        
        while ((currentRow != centerRow || currentCol != centerCol) && stepCount < maxSteps)
        {
            stepCount++;
            maze[currentRow, currentCol] = 0;
            
            var distToCenterRow = Math.Abs(centerRow - currentRow);
            var distToCenterCol = Math.Abs(centerCol - currentCol);
            
            // 60% move toward center, 40% move perpendicular for winding
            if (random.Next(100) < 60)
            {
                // Move toward center
                if (distToCenterRow > distToCenterCol && distToCenterRow > 0)
                {
                    currentRow += Math.Sign(centerRow - currentRow);
                }
                else if (distToCenterCol > 0)
                {
                    currentCol += Math.Sign(centerCol - currentCol);
                }
            }
            else
            {
                // Move perpendicular for winding effect
                if (distToCenterRow >= distToCenterCol)
                {
                    // Move horizontally
                    currentCol += random.Next(2) == 0 ? -1 : 1;
                }
                else
                {
                    // Move vertically
                    currentRow += random.Next(2) == 0 ? -1 : 1;
                }
            }
            
            currentRow = Math.Max(0, Math.Min(MazeSize - 1, currentRow));
            currentCol = Math.Max(0, Math.Min(MazeSize - 1, currentCol));
        }
        
        maze[centerRow, centerCol] = 0;
    }

    /// <summary>
    /// Carves a winding path between two ring openings
    /// </summary>
    private void CarveWindingPathBetweenRings(int[,] maze, int startRow, int startCol, int endRow, int endCol, 
        int centerRow, int centerCol, int outerDistance, int innerDistance)
    {
        var currentRow = startRow;
        var currentCol = startCol;
        
        // Move inward from outer ring opening
        for (var i = 0; i < 3; i++)
        {
            maze[currentRow, currentCol] = 0;
            var dRow = Math.Sign(centerRow - currentRow);
            var dCol = Math.Sign(centerCol - currentCol);
            
            if (Math.Abs(dRow) > 0) currentRow += dRow;
            else if (Math.Abs(dCol) > 0) currentCol += dCol;
            
            currentRow = Math.Max(0, Math.Min(MazeSize - 1, currentRow));
            currentCol = Math.Max(0, Math.Min(MazeSize - 1, currentCol));
        }
        
        // Create winding path toward inner ring opening
        var stepCount = 0;
        var maxSteps = 1000;
        
        while (stepCount < maxSteps)
        {
            stepCount++;
            maze[currentRow, currentCol] = 0;
            
            var distToEnd = Math.Abs(currentRow - endRow) + Math.Abs(currentCol - endCol);
            var distToCenter = Math.Max(Math.Abs(currentRow - centerRow), Math.Abs(currentCol - centerCol));
            
            // If we're within 3 cells of target or passed the inner ring, connect directly
            if (distToEnd <= 3 || distToCenter <= innerDistance + 3)
            {
                // Connect directly to target
                while (currentRow != endRow || currentCol != endCol)
                {
                    maze[currentRow, currentCol] = 0;
                    if (currentRow != endRow)
                        currentRow += Math.Sign(endRow - currentRow);
                    else if (currentCol != endCol)
                        currentCol += Math.Sign(endCol - currentCol);
                    
                    currentRow = Math.Max(0, Math.Min(MazeSize - 1, currentRow));
                    currentCol = Math.Max(0, Math.Min(MazeSize - 1, currentCol));
                }
                break;
            }
            
            // 50% move toward target, 50% move in a random direction for winding
            if (random.Next(100) < 50)
            {
                // Move toward target
                var dRow = Math.Sign(endRow - currentRow);
                var dCol = Math.Sign(endCol - currentCol);
                
                if (Math.Abs(dRow) > Math.Abs(dCol) && dRow != 0)
                {
                    currentRow += dRow;
                }
                else if (dCol != 0)
                {
                    currentCol += dCol;
                }
            }
            else
            {
                // Random direction for winding
                var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
                var (dRow, dCol) = directions[random.Next(directions.Length)];
                currentRow += dRow;
                currentCol += dCol;
            }
            
            currentRow = Math.Max(0, Math.Min(MazeSize - 1, currentRow));
            currentCol = Math.Max(0, Math.Min(MazeSize - 1, currentCol));
        }
        
        maze[endRow, endCol] = 0;
        
        // Clear small area around end point
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                var r = endRow + dr;
                var c = endCol + dc;
                if (r >= 0 && r < MazeSize && c >= 0 && c < MazeSize && maze[r, c] != 3)
                {
                    maze[r, c] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Carves a dead-end path that doesn't reach the inner ring (for incorrect openings)
    /// </summary>
    private void CarveDeadEndPath(int[,] maze, int startRow, int startCol, int centerRow, int centerCol, int currentDistance, int innerDistance)
    {
        var currentRow = startRow;
        var currentCol = startCol;
        
        // Path length: go partway toward inner ring but stop before reaching it
        // Scale based on distance between rings
        var distanceBetweenRings = currentDistance - innerDistance;
        var maxSteps = Math.Max(5, Math.Min(15, distanceBetweenRings / 2));
        var minDistance = innerDistance > 0 ? innerDistance + 8 : 5; // Don't get too close to inner ring
        
        for (var step = 0; step < maxSteps; step++)
        {
            maze[currentRow, currentCol] = 0;
            
            var distFromCenter = Math.Max(Math.Abs(currentRow - centerRow), Math.Abs(currentCol - centerCol));
            
            // If we're getting too close to inner ring, stop or move tangentially
            if (distFromCenter <= minDistance)
            {
                // Move tangentially (perpendicular to center direction) for a few more steps
                for (var i = 0; i < 3; i++)
                {
                    var perpendicular = GetPerpendicularDirection(currentRow, currentCol, centerRow, centerCol);
                    currentRow = Math.Max(1, Math.Min(MazeSize - 2, currentRow + perpendicular.dRow));
                    currentCol = Math.Max(1, Math.Min(MazeSize - 2, currentCol + perpendicular.dCol));
                    if (maze[currentRow, currentCol] == 1)
                        maze[currentRow, currentCol] = 0;
                }
                break;
            }
            
            // Move generally toward center with randomness
            var dRow = random.Next(3) - 1; // -1, 0, or 1
            var dCol = random.Next(3) - 1;
            
            if (dRow == 0 && dCol == 0)
            {
                dRow = random.Next(2) == 0 ? -1 : 1;
            }
            
            currentRow = Math.Max(1, Math.Min(MazeSize - 2, currentRow + dRow));
            currentCol = Math.Max(1, Math.Min(MazeSize - 2, currentCol + dCol));
        }
    }

    /// <summary>
    /// Gets a direction perpendicular to the direction toward center
    /// </summary>
    private (int dRow, int dCol) GetPerpendicularDirection(int row, int col, int centerRow, int centerCol)
    {
        var towardCenterRow = centerRow - row;
        var towardCenterCol = centerCol - col;
        
        // If moving toward center vertically, move horizontally
        if (Math.Abs(towardCenterRow) > Math.Abs(towardCenterCol))
        {
            return (0, random.Next(2) == 0 ? -1 : 1);
        }
        else
        {
            return (random.Next(2) == 0 ? -1 : 1, 0);
        }
    }

    /// <summary>
    /// Creates a 3-cell wide opening at the specified position
    /// </summary>
    private void CreateWideOpening(int[,] maze, int row, int col, int side, int centerRow, int centerCol)
    {
        // Create opening at main position
        if (row >= 0 && row < MazeSize && col >= 0 && col < MazeSize)
        {
            maze[row, col] = 0;
        }
        
        // Make it wider based on side
        switch (side)
        {
            case 0: // Top - widen horizontally
            case 2: // Bottom - widen horizontally
                if (col > 0 && col - 1 >= 0) maze[row, col - 1] = 0;
                if (col < MazeSize - 1 && col + 1 < MazeSize) maze[row, col + 1] = 0;
                break;
            case 1: // Right - widen vertically
            case 3: // Left - widen vertically
                if (row > 0 && row - 1 >= 0) maze[row - 1, col] = 0;
                if (row < MazeSize - 1 && row + 1 < MazeSize) maze[row + 1, col] = 0;
                break;
        }
    }

    /// <summary>
    /// Adds dead ends to make the maze more challenging
    /// Dead ends are short corridors that lead nowhere
    /// </summary>
    private void AddDeadEnds(int[,] maze, int centerRow, int centerCol, (int distance, int wallThickness, int openings)[] rings)
    {
        var deadEndCount = 150; // Number of dead ends to add
        var attempts = 0;
        var maxAttempts = deadEndCount * 10;
        var addedDeadEnds = 0;
        
        while (addedDeadEnds < deadEndCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Pick a random position
            var row = random.Next(MazeSize);
            var col = random.Next(MazeSize);
            
            // Check if it's a path (2) with at least one adjacent wall
            if (maze[row, col] != 2) continue;
            
            // Find adjacent walls where we can carve a dead end
            var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
            var possibleDirections = new List<(int dRow, int dCol)>();
            
            foreach (var (dRow, dCol) in directions)
            {
                var wallRow = row + dRow;
                var wallCol = col + dCol;
                
                if (wallRow >= 0 && wallRow < MazeSize && wallCol >= 0 && wallCol < MazeSize &&
                    maze[wallRow, wallCol] == 1)
                {
                    // Check if we can carve 2-4 cells in this direction
                    var canCarve = true;
                    var deadEndLength = random.Next(2, 5); // Random length 2-4
                    
                    for (var step = 1; step <= deadEndLength; step++)
                    {
                        var checkRow = row + dRow * step;
                        var checkCol = col + dCol * step;
                        
                        if (checkRow < 0 || checkRow >= MazeSize || checkCol < 0 || checkCol >= MazeSize ||
                            maze[checkRow, checkCol] != 1)
                        {
                            canCarve = false;
                            break;
                        }
                    }
                    
                    if (canCarve)
                    {
                        possibleDirections.Add((dRow, dCol));
                    }
                }
            }
            
            if (possibleDirections.Count > 0)
            {
                // Pick a random direction and carve the dead end
                var (dRow, dCol) = possibleDirections[random.Next(possibleDirections.Count)];
                var deadEndLength = random.Next(2, 5);
                
                for (var step = 1; step <= deadEndLength; step++)
                {
                    var carveRow = row + dRow * step;
                    var carveCol = col + dCol * step;
                    maze[carveRow, carveCol] = 0;
                }
                
                addedDeadEnds++;
            }
        }
    }

    /// <summary>
    /// Flood fills all accessible paths with value 2, starting from outer edge
    /// </summary>
    private void FloodFillPaths(int[,] maze)
    {
        var queue = new Queue<(int row, int col)>();
        var visited = new System.Collections.Generic.HashSet<(int row, int col)>();
        
        // Start from all edges that are empty (0)
        // Top and bottom edges
        for (var col = 0; col < MazeSize; col++)
        {
            if (maze[0, col] == 0)
            {
                queue.Enqueue((0, col));
                visited.Add((0, col));
            }
            if (maze[MazeSize - 1, col] == 0)
            {
                queue.Enqueue((MazeSize - 1, col));
                visited.Add((MazeSize - 1, col));
            }
        }
        
        // Left and right edges
        for (var row = 1; row < MazeSize - 1; row++)
        {
            if (maze[row, 0] == 0)
            {
                queue.Enqueue((row, 0));
                visited.Add((row, 0));
            }
            if (maze[row, MazeSize - 1] == 0)
            {
                queue.Enqueue((row, MazeSize - 1));
                visited.Add((row, MazeSize - 1));
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
                
                if (newRow >= 0 && newRow < MazeSize && 
                    newCol >= 0 && newCol < MazeSize &&
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
    /// Generates a maze and returns it as a string representation.
    /// </summary>
    public string GenerateMazeAsString()
    {
        var maze = GenerateMaze();
        var result = new System.Text.StringBuilder();

        for (var i = 0; i < MazeSize; i++)
        {
            for (var j = 0; j < MazeSize; j++)
            {
                result.Append(maze[i, j]);
            }
            if (i < MazeSize - 1)
            {
                result.AppendLine();
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Prints a small section of the maze for debugging (center area).
    /// Values: 0=empty, 1=wall, 2=path, 3=exit
    /// </summary>
    public void PrintMazeDebug(int[,] maze, int size = 40)
    {
        var centerRow = MazeSize / 2;
        var centerCol = MazeSize / 2;
        var startRow = Math.Max(0, centerRow - size / 2);
        var endRow = Math.Min(MazeSize, centerRow + size / 2);
        var startCol = Math.Max(0, centerCol - size / 2);
        var endCol = Math.Min(MazeSize, centerCol + size / 2);

        Console.WriteLine($"\n=== MAZE DEBUG ({startRow}-{endRow}, {startCol}-{endCol}) ===");
        Console.WriteLine("0=empty, 1=wall, 2=path, 3=exit");
        for (var i = startRow; i < endRow; i++)
        {
            for (var j = startCol; j < endCol; j++)
            {
                Console.Write(maze[i, j]);
                if (j < endCol - 1) Console.Write(",");
            }
            Console.WriteLine();
        }
        Console.WriteLine("=== END DEBUG ===\n");
    }


    /// <summary>
    /// Generates a maze with guaranteed solution path.
    /// Returns both the maze and the solution path coordinates.
    /// </summary>
    public (int[,] maze, List<(int row, int col)> solution) GenerateMazeWithSolution()
    {
        var maze = GenerateMaze();
        
        // Find the exit position (value 3)
        var exitPos = FindExitPosition(maze);
        
        if (!exitPos.HasValue)
        {
            // Fallback: if no exit found, return empty solution
            return (maze, new List<(int row, int col)>());
        }
        
        var startPos = GetRandomStartPosition(maze);
        var solution = FindPath(maze, startPos, exitPos.Value);
        
        return (maze, solution);
    }

    /// <summary>
    /// Finds the exit position in the maze (cell with value 3)
    /// </summary>
    private (int row, int col)? FindExitPosition(int[,] maze)
    {
        for (var row = 0; row < MazeSize; row++)
        {
            for (var col = 0; col < MazeSize; col++)
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
    public (int row, int col) GetRandomStartPosition(int[,] maze)
    {
        var validPositions = new List<(int row, int col)>();
        
        // Collect all valid path positions (value 2) on outer edges
        // Top and bottom edges
        for (var col = 0; col < MazeSize; col++)
        {
            if (maze[0, col] == 2) validPositions.Add((0, col));
            if (maze[MazeSize - 1, col] == 2) validPositions.Add((MazeSize - 1, col));
        }
        
        // Left and right edges
        for (var row = 1; row < MazeSize - 1; row++)
        {
            if (maze[row, 0] == 2) validPositions.Add((row, 0));
            if (maze[row, MazeSize - 1] == 2) validPositions.Add((row, MazeSize - 1));
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
    private static List<(int row, int col)> FindPath(int[,] maze, (int row, int col) start, (int row, int col) end)
    {
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

                if (newRow >= 0 && newRow < MazeSize && 
                    newCol >= 0 && newCol < MazeSize &&
                    !visited.Contains(newPos) &&
                    (maze[newRow, newCol] == 2 || maze[newRow, newCol] == 3 || maze[newRow, newCol] == 4)) // Can walk on paths (2), exit (3), and chests (4)
                {
                    visited.Add(newPos);
                    var newPath = new List<(int row, int col)>(path) { newPos };
                    queue.Enqueue((newRow, newCol, newPath));
                }
            }
        }

        return new List<(int row, int col)>();
    }

    /// <summary>
    /// Generates a classic maze using recursive backtracking algorithm
    /// This creates long connected walls instead of scattered blocks
    /// </summary>
    private void GenerateClassicMaze(int[,] maze)
    {
        // Start from center and work outward
        var startRow = MazeSize / 2;
        var startCol = MazeSize / 2;
        
        // Carve the starting cell
        maze[startRow, startCol] = 0;
        
        // Use recursive backtracking to create maze
        CarvePassagesFrom(maze, startRow, startCol);
    }

    /// <summary>
    /// Recursive backtracking to carve passages - creates long connected corridors
    /// </summary>
    private void CarvePassagesFrom(int[,] maze, int row, int col)
    {
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
            if (newRow < 1 || newRow >= MazeSize - 1 || newCol < 1 || newCol >= MazeSize - 1)
                continue;
            
            // If the target cell is a wall, carve a path to it
            if (maze[newRow, newCol] == 1)
            {
                // Carve the cell between
                maze[row + dRow, col + dCol] = 0;
                // Carve the target cell
                maze[newRow, newCol] = 0;
                
                // Recurse from the new cell
                CarvePassagesFrom(maze, newRow, newCol);
            }
        }
    }

    /// <summary>
    /// Clears the outer edge of the maze (2-3 cells deep) to create a clear starting area
    /// </summary>
    private void ClearOuterEdge(int[,] maze)
    {
        var edgeWidth = 3;
        
        for (var row = 0; row < MazeSize; row++)
        {
            for (var col = 0; col < MazeSize; col++)
            {
                // Check if cell is within edge distance from any border
                if (row < edgeWidth || row >= MazeSize - edgeWidth ||
                    col < edgeWidth || col >= MazeSize - edgeWidth)
                {
                    maze[row, col] = 0;
                }
            }
        }
    }

    /// <summary>
    /// Adds concentric square rings with specified number of openings
    /// Inner rings have fewer openings, creating a funnel effect
    /// </summary>
    private void AddConcentricRingsWithOpenings(int[,] maze, int centerRow, int centerCol, 
        (int distance, int wallThickness, int openings)[] rings)
    {
        foreach (var (distance, wallThickness, openings) in rings)
        {
            // Create the ring wall
            CreateSquareRing(maze, centerRow, centerCol, distance, wallThickness);
            
            // Create openings in the ring
            CreateRingOpenings(maze, centerRow, centerCol, distance, wallThickness, openings);
        }
    }

    /// <summary>
    /// Creates a square ring wall at the specified distance from center
    /// </summary>
    private void CreateSquareRing(int[,] maze, int centerRow, int centerCol, int distance, int thickness)
    {
        for (var t = 0; t < thickness; t++)
        {
            var currentDist = distance + t;
            
            for (var offset = -currentDist; offset <= currentDist; offset++)
            {
                // Top edge
                if (centerRow - currentDist >= 0)
                    maze[centerRow - currentDist, centerCol + offset] = 1;
                
                // Bottom edge
                if (centerRow + currentDist < MazeSize)
                    maze[centerRow + currentDist, centerCol + offset] = 1;
                
                // Left edge
                if (centerCol - currentDist >= 0)
                    maze[centerRow + offset, centerCol - currentDist] = 1;
                
                // Right edge
                if (centerCol + currentDist < MazeSize)
                    maze[centerRow + offset, centerCol + currentDist] = 1;
            }
        }
    }

    /// <summary>
    /// Creates openings in a ring wall
    /// </summary>
    private void CreateRingOpenings(int[,] maze, int centerRow, int centerCol, int distance, int thickness, int openingCount)
    {
        var openings = new List<(int row, int col, int side)>();
        
        // Sides: 0=top, 1=right, 2=bottom, 3=left
        for (var i = 0; i < openingCount; i++)
        {
            var attempts = 0;
            var maxAttempts = 100;
            
            while (attempts < maxAttempts)
            {
                attempts++;
                
                var side = random.Next(4);
                var offset = random.Next(-distance + thickness * 2, distance - thickness * 2);
                
                int row, col;
                switch (side)
                {
                    case 0: // Top
                        row = centerRow - distance;
                        col = centerCol + offset;
                        break;
                    case 1: // Right
                        row = centerRow + offset;
                        col = centerCol + distance;
                        break;
                    case 2: // Bottom
                        row = centerRow + distance;
                        col = centerCol + offset;
                        break;
                    default: // Left
                        row = centerRow + offset;
                        col = centerCol - distance;
                        break;
                }
                
                // Check bounds
                if (row < 0 || row >= MazeSize || col < 0 || col >= MazeSize)
                    continue;
                
                // Check if too close to another opening
                var tooClose = false;
                foreach (var (oRow, oCol, _) in openings)
                {
                    if (Math.Abs(oRow - row) + Math.Abs(oCol - col) < thickness * 3)
                    {
                        tooClose = true;
                        break;
                    }
                }
                
                if (!tooClose)
                {
                    openings.Add((row, col, side));
                    
                    // Carve the opening through all layers of the wall
                    for (var t = 0; t < thickness; t++)
                    {
                        int carveRow = row, carveCol = col;
                        
                        switch (side)
                        {
                            case 0: // Top - carve downward
                                carveRow = centerRow - distance + t;
                                break;
                            case 1: // Right - carve leftward
                                carveCol = centerCol + distance - t;
                                break;
                            case 2: // Bottom - carve upward
                                carveRow = centerRow + distance - t;
                                break;
                            case 3: // Left - carve rightward
                                carveCol = centerCol - distance + t;
                                break;
                        }
                        
                        if (carveRow >= 0 && carveRow < MazeSize && carveCol >= 0 && carveCol < MazeSize)
                        {
                            maze[carveRow, carveCol] = 0;
                        }
                    }
                    
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Adds dead ends to the maze for extra complexity
    /// </summary>
    private void AddDeadEndsToMaze(int[,] maze, int count)
    {
        var added = 0;
        var attempts = 0;
        var maxAttempts = count * 10;
        
        while (added < count && attempts < maxAttempts)
        {
            attempts++;
            
            var row = random.Next(1, MazeSize - 1);
            var col = random.Next(1, MazeSize - 1);
            
            // Find a path cell with adjacent wall
            if (maze[row, col] != 0 && maze[row, col] != 2)
                continue;
            
            // Try to carve a dead end into an adjacent wall
            var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
            var direction = directions[random.Next(directions.Length)];
            
            var length = random.Next(2, 6); // Dead end length 2-5
            var canCarve = true;
            
            // Check if we can carve in this direction
            for (var step = 1; step <= length; step++)
            {
                var checkRow = row + direction.Item1 * step;
                var checkCol = col + direction.Item2 * step;
                
                if (checkRow < 1 || checkRow >= MazeSize - 1 || checkCol < 1 || checkCol >= MazeSize - 1 ||
                    maze[checkRow, checkCol] != 1)
                {
                    canCarve = false;
                    break;
                }
            }
            
            if (canCarve)
            {
                // Carve the dead end
                for (var step = 1; step <= length; step++)
                {
                    var carveRow = row + direction.Item1 * step;
                    var carveCol = col + direction.Item2 * step;
                    maze[carveRow, carveCol] = 0;
                }
                
                added++;
            }
        }
    }

    /// <summary>
    /// Ensures there is a guaranteed path from the outer edge to the center
    /// Uses BFS to check connectivity and carves path if needed
    /// </summary>
    private void EnsurePathToCenter(int[,] maze, int centerRow, int centerCol)
    {
        // Check if center is already reachable from outer edge
        if (IsReachableFromEdge(maze, centerRow, centerCol))
        {
            return; // Already connected
        }
        
        // If not reachable, carve a winding path from edge to center
        CarveGuaranteedPath(maze, centerRow, centerCol);
    }

    /// <summary>
    /// Checks if a position is reachable from the outer edge using BFS
    /// </summary>
    private bool IsReachableFromEdge(int[,] maze, int targetRow, int targetCol)
    {
        var visited = new bool[MazeSize, MazeSize];
        var queue = new Queue<(int row, int col)>();
        
        // Start BFS from all outer edge cells that are paths (0)
        for (var i = 0; i < MazeSize; i++)
        {
            // Top and bottom edges
            if (maze[0, i] == 0)
            {
                queue.Enqueue((0, i));
                visited[0, i] = true;
            }
            if (maze[MazeSize - 1, i] == 0)
            {
                queue.Enqueue((MazeSize - 1, i));
                visited[MazeSize - 1, i] = true;
            }
            
            // Left and right edges
            if (maze[i, 0] == 0)
            {
                queue.Enqueue((i, 0));
                visited[i, 0] = true;
            }
            if (maze[i, MazeSize - 1] == 0)
            {
                queue.Enqueue((i, MazeSize - 1));
                visited[i, MazeSize - 1] = true;
            }
        }
        
        // BFS
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
        
        while (queue.Count > 0)
        {
            var (row, col) = queue.Dequeue();
            
            // Check if we reached the target
            if (row == targetRow && col == targetCol)
            {
                return true;
            }
            
            // Check if we're close to target
            if (Math.Abs(row - targetRow) <= 1 && Math.Abs(col - targetCol) <= 1)
            {
                return true;
            }
            
            // Explore neighbors
            foreach (var (dRow, dCol) in directions)
            {
                var newRow = row + dRow;
                var newCol = col + dCol;
                
                if (newRow >= 0 && newRow < MazeSize && newCol >= 0 && newCol < MazeSize &&
                    !visited[newRow, newCol] && (maze[newRow, newCol] == 0 || maze[newRow, newCol] == 3))
                {
                    visited[newRow, newCol] = true;
                    queue.Enqueue((newRow, newCol));
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// Carves a guaranteed winding path from outer edge to center
    /// </summary>
    private void CarveGuaranteedPath(int[,] maze, int centerRow, int centerCol)
    {
        // Start from a random edge position on the left side
        var startRow = random.Next(3, MazeSize - 3);
        var startCol = 0;
        
        // Make sure start is clear (startCol is 0, so +1 and +2 are always valid)
        maze[startRow, startCol] = 0;
        maze[startRow, 1] = 0;
        maze[startRow, 2] = 0;
        
        var currentRow = startRow;
        var currentCol = 2;
        
        // Carve toward center with some randomness for winding
        while (Math.Abs(currentRow - centerRow) > 2 || Math.Abs(currentCol - centerCol) > 2)
        {
            maze[currentRow, currentCol] = 0;
            
            // Determine direction toward center with some randomness
            var dRow = 0;
            var dCol = 0;
            
            if (Math.Abs(currentRow - centerRow) > Math.Abs(currentCol - centerCol))
            {
                // Move vertically toward center
                dRow = currentRow < centerRow ? 1 : -1;
                
                // Sometimes move horizontally for winding
                if (random.Next(4) == 0 && Math.Abs(currentCol - centerCol) > 5)
                {
                    dRow = 0;
                    dCol = currentCol < centerCol ? 1 : -1;
                }
            }
            else
            {
                // Move horizontally toward center
                dCol = currentCol < centerCol ? 1 : -1;
                
                // Sometimes move vertically for winding
                if (random.Next(4) == 0 && Math.Abs(currentRow - centerRow) > 5)
                {
                    dCol = 0;
                    dRow = currentRow < centerRow ? 1 : -1;
                }
            }
            
            currentRow += dRow;
            currentCol += dCol;
            
            // Ensure bounds
            currentRow = Math.Max(1, Math.Min(MazeSize - 2, currentRow));
            currentCol = Math.Max(1, Math.Min(MazeSize - 2, currentCol));
            
            maze[currentRow, currentCol] = 0;
        }
        
        // Clear area around center
        for (var dr = -2; dr <= 2; dr++)
        {
            for (var dc = -2; dc <= 2; dc++)
            {
                var r = centerRow + dr;
                var c = centerCol + dc;
                if (r >= 0 && r < MazeSize && c >= 0 && c < MazeSize && maze[r, c] != 3)
                {
                    maze[r, c] = 0;
                }
            }
        }
    }
}

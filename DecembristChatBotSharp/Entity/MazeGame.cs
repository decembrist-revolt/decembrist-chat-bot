using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

/// <summary>
/// Represents an active maze game in a chat
/// </summary>
public record MazeGame(
    [property: BsonId] MazeGame.CompositeId Id,
    int[,] Maze,
    DateTime CreatedAt,
    (int row, int col) ExitPosition,
    bool IsFinished,
    long? WinnerId
)
{
    public record CompositeId(long ChatId, int MessageId);
}

/// <summary>
/// Represents a player in a maze game
/// </summary>
public record MazeGamePlayer(
    [property: BsonId] MazeGamePlayer.CompositeId Id,
    (int row, int col) Position,
    (int row, int col) SpawnPosition,
    string Color, // Hex color code for player visualization
    int ViewRadius,
    DateTime JoinedAt,
    DateTime LastMoveAt,
    bool IsAlive,
    MazePlayerInventory Inventory,
    bool HasReceivedLastUpdate,
    int? LastPhotoMessageId // MessageId последней отправленной фотографии для удаления
)
{
    public record CompositeId(long ChatId, int MessageId, long TelegramId);
}

/// <summary>
/// Inventory of items a player can have in the maze
/// </summary>
public record MazePlayerInventory(
    int Swords,
    int Shields, 
    int Shovels,
    int ViewExpanders // Увеличивает радиус видимости на 1
)
{
    public static MazePlayerInventory Empty => new(0, 0, 0, 0);
}

/// <summary>
/// Types of items that can be found in chests
/// </summary>
public enum MazeItemType
{
    Sword = 0,
    Shield = 1,
    Shovel = 2,
    ViewExpander = 3 // Увеличивает область видимости на 1
}

/// <summary>
/// Directions for maze movement
/// </summary>
public enum MazeDirection
{
    Up,
    Down,
    Left,
    Right
}


using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MazeGameRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public Task<Unit> EnsureIndexes()
    {
        // No special indexes needed for now
        return Task.FromResult(unit);
    }

    public async Task<bool> CreateGame(MazeGame game, IMongoSession? session = null)
    {
        var collection = GetGameCollection();
        var filter = Builders<MazeGame>.Filter.Eq(x => x.Id, game.Id);
        var options = new UpdateOptions { IsUpsert = true };

        var update = Builders<MazeGame>.Update
            .Set(x => x.Maze, game.Maze)
            .Set(x => x.CreatedAt, game.CreatedAt)
            .Set(x => x.ExitPosition, game.ExitPosition)
            .Set(x => x.IsFinished, game.IsFinished)
            .Set(x => x.WinnerId, game.WinnerId);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount > 0),
            ex =>
            {
                Log.Error(ex, "Failed to create maze game for chat {0}", game.Id.ChatId);
                return false;
            });
    }

    public async Task<Option<MazeGame>> GetGame(MazeGame.CompositeId id)
    {
        var collection = GetGameCollection();
        return await collection
            .Find(game => game.Id.ChatId == id.ChatId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to get maze game {0}", id);
                return None;
            });
    }

    public async Task<bool> FinishGame(MazeGame.CompositeId id, long winnerId, IMongoSession? session = null)
    {
        var collection = GetGameCollection();
        var filter = Builders<MazeGame>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGame>.Update
            .Set(x => x.IsFinished, true)
            .Set(x => x.WinnerId, winnerId);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to finish maze game {0}", id);
                return false;
            });
    }

    public async Task<bool> UpdateMaze(MazeGame.CompositeId id, int[,] maze, IMongoSession? session = null)
    {
        var collection = GetGameCollection();
        var filter = Builders<MazeGame>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGame>.Update.Set(x => x.Maze, maze);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to update maze for game {0}", id);
                return false;
            });
    }

    public async Task<bool> AddPlayer(MazeGamePlayer player, IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, player.Id);
        var options = new UpdateOptions { IsUpsert = true };

        var update = Builders<MazeGamePlayer>.Update
            .Set(x => x.Position, player.Position)
            .Set(x => x.SpawnPosition, player.SpawnPosition)
            .Set(x => x.Color, player.Color)
            .Set(x => x.ViewRadius, player.ViewRadius)
            .Set(x => x.JoinedAt, player.JoinedAt)
            .Set(x => x.LastMoveAt, player.LastMoveAt)
            .Set(x => x.IsAlive, player.IsAlive)
            .Set(x => x.Inventory, player.Inventory)
            .Set(x => x.HasReceivedLastUpdate, player.HasReceivedLastUpdate);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount > 0),
            ex =>
            {
                Log.Error(ex, "Failed to add maze player {0}", player.Id);
                return false;
            });
    }

    public async Task<Option<MazeGamePlayer>> GetPlayer(MazeGamePlayer.CompositeId id)
    {
        var collection = GetPlayerCollection();
        return await collection
            .Find(player => player.Id == id)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to get maze player {0}", id);
                return None;
            });
    }

    public async Task<List<MazeGamePlayer>> GetAllPlayersInGame(long chatId)
    {
        var collection = GetPlayerCollection();
        return await collection
            .Find(player => player.Id.ChatId == chatId && player.Id.ChatId == chatId)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to get all players for game {0}", chatId);
                return [];
            });
    }

    public async Task<bool> UpdatePlayerPosition(MazeGamePlayer.CompositeId id, (int row, int col) newPosition,
        IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGamePlayer>.Update
            .Set(x => x.Position, newPosition)
            .Set(x => x.LastMoveAt, DateTime.UtcNow)
            .Set(x => x.HasReceivedLastUpdate, false);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to update player position {0}", id);
                return false;
            });
    }

    public async Task<bool> UpdatePlayerInventory(MazeGamePlayer.CompositeId id, MazePlayerInventory inventory,
        IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGamePlayer>.Update.Set(x => x.Inventory, inventory);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to update player inventory {0}", id);
                return false;
            });
    }

    public async Task<bool> KillPlayer(MazeGamePlayer.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var playerOpt = await GetPlayer(id);

        return await playerOpt.MatchAsync(async player =>
        {
            var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
            var update = Builders<MazeGamePlayer>.Update
                .Set(x => x.IsAlive, false)
                .Set(x => x.Position, player.SpawnPosition);

            var updateTask = not(session.IsNull())
                ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
                : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

            return await updateTask.ToTryAsync().Match(
                result => result.IsAcknowledged && result.ModifiedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to kill player {0}", id);
                    return false;
                });
        }, () => Task.FromResult(false));
    }

    public async Task<bool> RevivePlayer(MazeGamePlayer.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGamePlayer>.Update.Set(x => x.IsAlive, true);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount > 0,
            ex =>
            {
                Log.Error(ex, "Failed to revive player {0}", id);
                return false;
            });
    }

    public async Task<bool> MarkPlayerUpdateSent(MazeGamePlayer.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGamePlayer>.Update.Set(x => x.HasReceivedLastUpdate, true);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync()
            .Match(result => result.IsAcknowledged && result.ModifiedCount > 0, ex =>
            {
                Log.Error(ex, "Failed to mark player update sent {0}", id);
                return false;
            });
    }

    public async Task<bool> RemovePlayers(long chatId, IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id.ChatId, chatId);
        var deleteManyAsync = session is not null
            ? collection.DeleteManyAsync(session, filter, cancellationToken: cancelToken.Token)
            : collection.DeleteManyAsync(filter, cancellationToken: cancelToken.Token);
        return await deleteManyAsync
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to remove players for chat {0}", chatId);
                    return false;
                });
    }

    public async Task<bool> UpdatePlayerLastPhotoMessageId(MazeGamePlayer.CompositeId id, int? photoMessageId,
        IMongoSession? session = null)
    {
        var collection = GetPlayerCollection();
        var filter = Builders<MazeGamePlayer>.Filter.Eq(x => x.Id, id);
        var update = Builders<MazeGamePlayer>.Update.Set(x => x.LastPhotoMessageId, photoMessageId);

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, null, cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, null, cancelToken.Token);

        return await updateTask.ToTryAsync()
            .Match(result => result.IsAcknowledged && result.ModifiedCount > 0, ex =>
            {
                Log.Error(ex, "Failed to update player last photo message id {0}", id);
                return false;
            });
    }

    public async Task<Option<MazeGame>> GetActiveGameForChat(long chatId)
    {
        var collection = GetGameCollection();
        return await collection
            .Find(game => game.Id.ChatId == chatId && !game.IsFinished)
            .SortByDescending(game => game.CreatedAt)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to get active game for chat {0}", chatId);
                return None;
            });
    }

    public async Task<bool> RemoveGameForChat(long chatId, IMongoSession? session = null)
    {
        var collection = GetGameCollection();
        var filter = Builders<MazeGame>.Filter.Eq(x => x.Id.ChatId, chatId);

        var deleteTask = session is not null
            ? collection.DeleteOneAsync(session, filter, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(filter, cancelToken.Token);
        return await deleteTask
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to remove game for chat {0}", chatId);
                    return false;
                });
    }

    private IMongoCollection<MazeGame> GetGameCollection() =>
        db.GetCollection<MazeGame>(nameof(MazeGame));

    private IMongoCollection<MazeGamePlayer> GetPlayerCollection() =>
        db.GetCollection<MazeGamePlayer>(nameof(MazeGamePlayer));
}
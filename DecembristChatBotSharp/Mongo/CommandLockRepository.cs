using DecembristChatBotSharp.Entity;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class CommandLockRepository(
    AppConfig appConfig,
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    /// <returns>True if lock found</returns>
    public async Task<bool> FindLock(long chatId, string command, string? arguments = null, long? telegramId = null)
    {
        var collection = GetCollection();
        var errorOrOption = await collection
            .Find(@lock => @lock.Id.ChatId == chatId
                           && @lock.Id.Command == command
                           && @lock.Id.Arguments == arguments
                           && @lock.Id.TelegramId == telegramId)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Map(Optional)
            .ToEither();

        if (errorOrOption.IsLeft)
        {
            errorOrOption.IfLeft(ex =>
                Log.Error(ex, "Failed to find lock for command {0} in chat {1}", command, chatId));
            return true;
        }

        var maybeLock = errorOrOption.ValueUnsafe();
        if (maybeLock.IsNone) return false;

        return await maybeLock
            .Filter(@lock => DateTime.UtcNow > @lock.ExpiredTime)
            .MapAsync(_ => RemoveLock(chatId, command, arguments, telegramId))
            .Match(removed => !removed, () => true);
    }

    /// <summary>
    /// Acquire lock for command
    /// </summary>
    /// <returns>True if lock acquired</returns>
    public async Task<bool> AcquireLock(long chatId, string command, string? arguments = null, long? telegramId = null)
    {
        var collection = GetCollection();
        var commandIntervalSeconds = appConfig.CommandConfig.CommandIntervalSeconds;
        var expiredTime = DateTime.UtcNow.AddSeconds(commandIntervalSeconds);
        var commandLock = new CommandLock(
            new CommandLock.CompositeId(chatId, command, arguments, telegramId),
            expiredTime);

        return await collection.InsertOneAsync(commandLock, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(_ => true, ex =>
            {
                Log.Error(ex, "Failed to acquire lock for command {0} and chatId {1}", command, chatId);
                return false;
            });
    }

    /// <summary>
    /// Find lock and acquire it if not found
    /// </summary>
    /// <returns>True if lock acquired</returns>
    public async Task<bool> TryAcquire(long chatId, string command, string? arguments = null, long? telegramId = null)
    {
        var lockFound = await FindLock(chatId, command, arguments, telegramId);
        if (lockFound) return false;

        return await AcquireLock(chatId, command, arguments, telegramId);
    }

    public async Task<bool> RemoveLock(long chatId, string command, string? arguments = null, long? telegramId = null)
    {
        var collection = GetCollection();
        return await collection.DeleteOneAsync(@lock => @lock.Id.ChatId == chatId
                                                        && @lock.Id.Command == command
                                                        && @lock.Id.Arguments == arguments
                                                        && @lock.Id.TelegramId == telegramId,
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to remove lock for command {0} and chatId {1}", command, chatId);
                    return false;
                });
    }

    private IMongoCollection<CommandLock> GetCollection() => db.GetCollection<CommandLock>(nameof(CommandLock));
}
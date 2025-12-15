using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class QuizSubtopicHistoryRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    private IMongoCollection<QuizSubtopicHistory> GetCollection() =>
        db.GetCollection<QuizSubtopicHistory>(nameof(QuizSubtopicHistory));

    /// <summary>
    /// Get the global subtopic history
    /// </summary>
    public async Task<Option<QuizSubtopicHistory>> GetHistory()
    {
        var collection = GetCollection();
        return await collection
            .Find(h => h.Id == QuizSubtopicHistory.GlobalId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                history => history != null ? Some(history) : None,
                ex =>
                {
                    Log.Error(ex, "Failed to get quiz subtopic history");
                    return None;
                });
    }

    /// <summary>
    /// Add a subtopic to history
    /// </summary>
    public async Task<bool> AddSubtopic(string subtopic, int maxHistory)
    {
        var collection = GetCollection();
        
        var history = await GetHistory();
        
        return await history.MatchAsync(
            async existing =>
            {
                // Add new subtopic and trim to max limit
                var updatedList = existing.RecentSubtopics.Append(subtopic).TakeLast(maxHistory).ToList();
                
                var filter = Builders<QuizSubtopicHistory>.Filter.Eq(h => h.Id, QuizSubtopicHistory.GlobalId);
                var update = Builders<QuizSubtopicHistory>.Update
                    .Set(h => h.RecentSubtopics, updatedList)
                    .Set(h => h.LastUpdatedUtc, DateTime.UtcNow);

                return await collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token)
                    .ToTryAsync()
                    .Match(
                        result => result.IsAcknowledged && result.ModifiedCount > 0,
                        ex =>
                        {
                            Log.Error(ex, "Failed to update quiz subtopic history");
                            return false;
                        });
            },
            async () =>
            {
                // Create new history
                var newHistory = new QuizSubtopicHistory(
                    QuizSubtopicHistory.GlobalId,
                    [subtopic],
                    DateTime.UtcNow
                );

                return await collection.InsertOneAsync(newHistory, cancellationToken: cancelToken.Token)
                    .ToTryAsync()
                    .Match(
                        _ => true,
                        ex =>
                        {
                            Log.Error(ex, "Failed to create quiz subtopic history");
                            return false;
                        });
            });
    }

    /// <summary>
    /// Clear all subtopic history
    /// </summary>
    public async Task<bool> ClearHistory()
    {
        var collection = GetCollection();
        var filter = Builders<QuizSubtopicHistory>.Filter.Eq(h => h.Id, QuizSubtopicHistory.GlobalId);
        var update = Builders<QuizSubtopicHistory>.Update
            .Set(h => h.RecentSubtopics, new List<string>())
            .Set(h => h.LastUpdatedUtc, DateTime.UtcNow);

        return await collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged,
                ex =>
                {
                    Log.Error(ex, "Failed to clear quiz subtopic history");
                    return false;
                });
    }
}


using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class QuizRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    private IMongoCollection<QuizQuestion> GetQuestionCollection() =>
        db.GetCollection<QuizQuestion>(nameof(QuizQuestion));

    /// <summary>
    /// Save a new quiz question for a chat
    /// </summary>
    public async Task<bool> SaveQuestion(QuizQuestion question)
    {
        var collection = GetQuestionCollection();
        return await collection.InsertOneAsync(question, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to save quiz question for chat {ChatId}", question.Id.ChatId);
                    return false;
                });
    }

    /// <summary>
    /// Get active quiz question for a chat
    /// </summary>
    public async Task<Option<QuizQuestion>> GetActiveQuestion(long chatId)
    {
        var collection = GetQuestionCollection();
        return await collection
            .Find(q => q.Id.ChatId == chatId)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                question => question != null ? Some(question) : None,
                ex =>
                {
                    Log.Error(ex, "Failed to get active quiz question for chat {0}", chatId);
                    return None;
                });
    }

    /// <summary>
    /// Delete quiz question
    /// </summary>
    public async Task<bool> DeleteQuestion(QuizQuestion.CompositeId id)
    {
        var collection = GetQuestionCollection();
        return await collection.DeleteOneAsync(q => q.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete quiz question {QuestionId}", id.QuestionId);
                    return false;
                });
    }

    /// <summary>
    /// Add user answer to the question document
    /// </summary>
    public async Task<bool> AddAnswer(long chatId, QuizAnswerData answer)
    {
        var collection = GetQuestionCollection();
        var filter = Builders<QuizQuestion>.Filter.Eq(q => q.Id.ChatId, chatId);
        var update = Builders<QuizQuestion>.Update.Push(q => q.Answers, answer);

        return await collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.ModifiedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to add answer for user {UserId} in chat {ChatId}",
                        answer.TelegramId, chatId);
                    return false;
                });
    }

    /// <summary>
    /// Get all questions with pending answers for validation
    /// </summary>
    public async Task<Arr<QuizQuestion>> GetQuestionsWithPendingAnswers()
    {
        var collection = GetQuestionCollection();
        var filter = Builders<QuizQuestion>.Filter.SizeGt(q => q.Answers, 0);

        return await collection
            .Find(filter)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                list => list.ToArr(),
                ex =>
                {
                    Log.Error(ex, "Failed to get questions with pending answers");
                    return Arr<QuizQuestion>.Empty;
                });
    }

    public async Task<Arr<QuizQuestion>> GetAllQuestions() =>
        await GetQuestionCollection()
            .Find(_ => true)
            .ToListAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(
                list => list.ToArr(),
                ex =>
                {
                    Log.Error(ex, "Failed to get questions");
                    return Arr<QuizQuestion>.Empty;
                });

    /// <summary>
    /// Remove specific answer from question after validation (by messageId)
    /// </summary>
    public async Task<bool> RemoveAnswer(long chatId, int messageId)
    {
        var collection = GetQuestionCollection();
        var filter = Builders<QuizQuestion>.Filter.Eq(q => q.Id.ChatId, chatId);
        var update = Builders<QuizQuestion>.Update.PullFilter(
            q => q.Answers,
            a => a.MessageId == messageId);

        return await collection.UpdateOneAsync(filter, update, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                result => result.IsAcknowledged && result.ModifiedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to remove answer with messageId {MessageId} in chat {ChatId}",
                        messageId, chatId);
                    return false;
                });
    }
}
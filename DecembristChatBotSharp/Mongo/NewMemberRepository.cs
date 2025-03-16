using DecembristChatBotSharp.Entity;
using LanguageExt.Common;
using MongoDB.Driver;

namespace DecembristChatBotSharp.Mongo;

public class NewMemberRepository(MongoDatabase db, CancellationTokenSource cancelToken)
{
    public TryAsync<Unit> AddNewMember(long telegramId, string username, long chatId, int welcomeMessageId)
    {
        var newMembers = db.GetCollection<NewMember>(nameof(NewMember));
        var newMember = new NewMember(
            new NewMember.CompositeId(telegramId, chatId),
            username,
            welcomeMessageId,
            DateTime.UtcNow);
        return TryAsync(async () =>
        {
            await newMembers.InsertOneAsync(newMember);
            return unit;
        });
    }

    public TryAsync<List<NewMember>> GetNewMembers(DateTime olderThan)
    {
        var newMembers = db.GetCollection<NewMember>(nameof(NewMember));
        return TryAsync(newMembers.Find(member => member.EnterDate < olderThan).ToListAsync(cancelToken.Token));
    }

    public EitherAsync<Error, NewMember> FindNewMember(NewMember.CompositeId id)
    {
        var newMembers = db.GetCollection<NewMember>(nameof(NewMember));
        return TryAsync(newMembers.Find(member => member.Id == id).SingleOrDefaultAsync(cancelToken.Token)!)
            .ToEither();
    }

    public TryAsync<bool> RemoveNewMember(NewMember.CompositeId id)
    {
        var newMembers = db.GetCollection<NewMember>(nameof(NewMember));
        var tryResult = TryAsync(newMembers.DeleteOneAsync(member => member.Id == id, cancelToken.Token));
        return tryResult.Map(result => result.DeletedCount > 0);
    }

    public TryAsync<UpdateResult> UpdateNewMemberRetries(NewMember.CompositeId id, int retryCount)
    {
        var newMembers = db.GetCollection<NewMember>(nameof(NewMember));
        return TryAsync(newMembers.UpdateOneAsync(
            member => member.Id == id,
            Builders<NewMember>.Update.Set(member => member.CaptchaRetryCount, retryCount),
            cancellationToken: cancelToken.Token));
    }
}
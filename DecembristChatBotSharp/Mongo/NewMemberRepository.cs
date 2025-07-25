﻿using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using LanguageExt.Common;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class NewMemberRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    public TryAsync<Unit> AddNewMember(long telegramId, string username, long chatId, int welcomeMessageId)
    {
        var newMembers = GetCollection();
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
        var newMembers = GetCollection();
        return TryAsync(newMembers.Find(member => member.EnterDate < olderThan).ToListAsync(cancelToken.Token));
    }

    public EitherAsync<Error, Option<NewMember>> FindNewMember(NewMember.CompositeId id)
    {
        var newMembers = GetCollection();
        return TryAsync(newMembers.Find(member => member.Id == id).SingleOrDefaultAsync(cancelToken.Token))
            .Map(Optional)
            .ToEither();
    }

    public Task<bool> RemoveNewMember(NewMember.CompositeId id) =>
        GetCollection().DeleteOneAsync(member => member.Id == id, cancelToken.Token)
            .ToTryAsync()
            .Match(result =>
                {
                    Log.Information("Successfully to delete new member {0}", id);
                    return result.DeletedCount > 0;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to delete new member {0}", id);
                    return false;
                });

    public async Task<bool> AddMemberItem(NewMember newMember, IMongoSession? session = null)
    {
        var collection = GetCollection();

        var update = Builders<NewMember>.Update
            .Set(member => member.WelcomeMessageId, newMember.WelcomeMessageId)
            .Set(member => member.CaptchaRetryCount, newMember.CaptchaRetryCount);
        var options = new UpdateOptions { IsUpsert = true };

        Expression<Func<NewMember, bool>> findExpr = member => member.Id == newMember.Id;
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, findExpr, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add new member {0}", newMember);
                return false;
            });
    }

    private IMongoCollection<NewMember> GetCollection() => db.GetCollection<NewMember>(nameof(NewMember));
}
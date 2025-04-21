﻿using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class FastReplyRepository(
    MongoDatabase db,
    CancellationTokenSource cancelToken) : IRepository
{
    public async Task<Option<FastReply>> FindOne(long chatId, string message, FastReplyType type)
    {
        var collection = GetCollection();
        var messageText = type == FastReplyType.Text ? message.ToLowerInvariant() : message;

        return await collection
            .Find(reply => reply.Id.ChatId == chatId
                           && reply.Id.Message == messageText
                           && reply.MessageType == type)
            .FirstOrDefaultAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to find fast reply for message {0} with type {1}", message, type);
                return None;
            });
    }
    
    public async Task<InsertResult> AddFastReply(FastReply fastReply, IClientSessionHandle session)
    {
        var collection = GetCollection();

        var tryFind = await collection.Find(reply => reply.Id == fastReply.Id)
            .SingleOrDefaultAsync(cancelToken.Token)
            .ToTryOption();

        if (tryFind.IsSome()) return InsertResult.Duplicate;
        
        return await collection
            .InsertOneAsync(session, fastReply, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => InsertResult.Success,
                ex =>
                {
                    Log.Error(ex, "Failed to add fast reply {0}", fastReply);
                    return InsertResult.Failed;
                });
    }

    public async Task<bool> DeleteFastReply(FastReply.CompositeId id, IMongoSession? session = null)
    {
        var collection = GetCollection();
        var taskResult = session.IsNull()
            ? collection.DeleteOneAsync(m => m.Id == id, cancellationToken: cancelToken.Token)
            : collection.DeleteOneAsync(session, m => m.Id == id, cancellationToken: cancelToken.Token);
        return await taskResult
            .ToTryAsync().Match(result => result.DeletedCount > 0,
                ex =>
                {
                    Log.Error(ex, "Failed to delete fast reply: {id} in fast reply collection", id);
                    return false;
                });
    }

    private IMongoCollection<FastReply> GetCollection() => db.GetCollection<FastReply>(nameof(FastReply));
    
    public enum InsertResult
    {
        Success,
        Duplicate,
        Failed
    }
}
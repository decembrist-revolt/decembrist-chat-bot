﻿using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using Lamar;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Serilog;

namespace DecembristChatBotSharp.Mongo;

[Singleton]
public class MemberItemRepository(MongoDatabase db, CancellationTokenSource cancelToken) : IRepository
{
    /// <returns>True if item was added</returns>
    public async Task<bool> AddMemberItem(
        long chatId,
        long telegramId,
        MemberItemType type,
        IMongoSession? session = null,
        int countItems = 1)
    {
        if (countItems <= 0) return false;
        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, type);
        var update = Builders<MemberItem>.Update.Inc(item => item.Count, countItems);
        var options = new UpdateOptions { IsUpsert = true };

        Expression<Func<MemberItem, bool>> findExpr = item => item.Id == id;
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, findExpr, update, options, cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, options, cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add item {0} to {1} in chat {2}", type, telegramId, chatId);
                return false;
            });
    }

    public async Task<bool> ChangeOwnerUniqueItem(
        long chatId,
        long telegramId,
        MemberItemType type,
        IMongoSession? session = null)
    {
        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, type);
        var update = Builders<MemberItem>.Update
            .Set(item => item.Id, id)
            .Set(item => item.Count, 1);

        var filter = Builders<MemberItem>.Filter.And(
            Builders<MemberItem>.Filter.Eq(item => item.Id.Type, type),
            Builders<MemberItem>.Filter.Gte(item => item.Count, 1));
        var options = new UpdateOptions { IsUpsert = true };

        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, filter, update, options, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(filter, update, options, cancellationToken: cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && (result.UpsertedId != null || result.ModifiedCount == 1),
            ex =>
            {
                Log.Error(ex, "Failed to add unique item {0} from {1} in chat {2}", type, telegramId, chatId);
                return false;
            });
    }

    public async Task<long> AddMemberItems(
        long chatId,
        Arr<long> telegramIds,
        MemberItemType type,
        IMongoSession? session = null)
    {
        if (telegramIds.IsEmpty) return 0;

        var collection = GetCollection();

        var requests =
            from telegramId in telegramIds
            let id = new MemberItem.CompositeId(telegramId, chatId, type)
            let filter = Builders<MemberItem>.Filter.Eq(item => item.Id, id)
            let update = Builders<MemberItem>.Update.Inc(item => item.Count, 1)
            select new UpdateOneModel<MemberItem>(filter, update) { IsUpsert = true };

        var updateTask = not(session.IsNull())
            ? collection.BulkWriteAsync(session, requests, cancellationToken: cancelToken.Token)
            : collection.BulkWriteAsync(requests, cancellationToken: cancelToken.Token);

        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged ? result.Upserts.Count + result.ModifiedCount : 0,
            ex =>
            {
                Log.Error(ex, "Failed to add item {0} multiple members in chat {1}", type, chatId);
                return 0;
            });
    }

    /// <returns>True if item was removed, false if item was not found or count was 0</returns>
    public async Task<bool> RemoveMemberItem(
        long chatId,
        long telegramId,
        MemberItemType type,
        IMongoSession? session = null,
        int countItems = -1)
    {
        if (countItems > -1) return false;

        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, type);
        var update = Builders<MemberItem>.Update.Inc(item => item.Count, countItems);

        Expression<Func<MemberItem, bool>> findExpr = item =>
            item.Id == id && item.Count > 0 && item.Count >= -countItems;
        var updateTask = not(session.IsNull())
            ? collection.UpdateOneAsync(session, findExpr, update, cancellationToken: cancelToken.Token)
            : collection.UpdateOneAsync(findExpr, update, cancellationToken: cancelToken.Token);
        return await updateTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount == 1,
            ex =>
            {
                Log.Error(ex, "Failed to remove item {0} from {1} in chat {2}", type, telegramId, chatId);
                return false;
            });
    }

    public async Task<bool> RemoveMemberItems(
        long chatId,
        long telegramId,
        List<ItemQuantity> itemsToRemove,
        IMongoSession? session = null)
    {
        if (itemsToRemove.Count <= 0 || itemsToRemove.Any(x => x.Quantity <= 0))
        {
            Log.Information("Failed to remove items from {0} in chat {1}, map is empty", telegramId, chatId);
            return false;
        }

        var collection = GetCollection();
        var updates = new List<UpdateOneModel<MemberItem>>();

        foreach (var (type, count) in itemsToRemove)
        {
            var id = new MemberItem.CompositeId(telegramId, chatId, type);
            var filter = Builders<MemberItem>.Filter.And(
                Builders<MemberItem>.Filter.Eq(item => item.Id, id),
                Builders<MemberItem>.Filter.Gte(item => item.Count, count)
            );

            var update = Builders<MemberItem>.Update.Inc(item => item.Count, -count);
            updates.Add(new UpdateOneModel<MemberItem>(filter, update));
        }

        var bulkTask = session != null
            ? collection.BulkWriteAsync(session, updates, cancellationToken: cancelToken.Token)
            : collection.BulkWriteAsync(updates, cancellationToken: cancelToken.Token);

        return await bulkTask.ToTryAsync().Match(
            result => result.IsAcknowledged && result.ModifiedCount == itemsToRemove.Count,
            ex =>
            {
                Log.Error(ex, "Failed to remove items from {0} in chat {1}", telegramId, chatId);
                return false;
            });
    }

    public async Task<Map<MemberItemType, int>> GetItems(long chatId, long telegramId)
    {
        var collection = GetCollection();

        Expression<Func<MemberItem, bool>> filter = item =>
            item.Id.TelegramId == telegramId && item.Id.ChatId == chatId && item.Count > 0;

        return await collection
            .Find(filter)
            .Project(item => new KeyValuePair<MemberItemType, int>(item.Id.Type, item.Count))
            .ToListAsync()
            .ToTryAsync()
            .Match(MapExtensions.ToMap, ex =>
            {
                Log.Error(ex, "Failed to get items for telegramId:{0}, chatId: {1} ", telegramId, chatId);
                return [];
            });
    }

    public async Task<bool> RemoveAllItemsForChat(long chatId, MemberItemType itemType, IMongoSession? session = null)
    {
        Expression<Func<MemberItem, bool>> filter = x => x.Id.ChatId == chatId && x.Id.Type == itemType;
        var collection = GetCollection();
        var query = session != null
            ? collection.DeleteManyAsync(session, filter, cancellationToken: cancelToken.Token)
            : collection.DeleteManyAsync(filter, cancelToken.Token);
        return await query
            .ToTryAsync()
            .Match(s => s.IsAcknowledged, ex =>
            {
                Log.Error(ex, "Failed to delete all item: {0} in chat: {1}", itemType, chatId);
                return false;
            });
    }

    public async Task<bool> IsUserHasItem(long chatId, long telegramId, MemberItemType itemType,
        IMongoSession? session = null, int countItem = 1)
    {
        if (countItem <= 0) return false;
        var collection = GetCollection();

        var id = new MemberItem.CompositeId(telegramId, chatId, itemType);
        var filter = Builders<MemberItem>.Filter.And(
            Builders<MemberItem>.Filter.Eq(reply => reply.Id, id),
            Builders<MemberItem>.Filter.Gte(reply => reply.Count, countItem));

        var query = session == null ? collection.Find(filter) : collection.Find(session, filter);
        return await query
            .AnyAsync(cancelToken.Token)
            .ToTryAsync()
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find member item with telegramId {0}", id);
                return false;
            });
    }

    private IMongoCollection<MemberItem> GetCollection() => db.GetCollection<MemberItem>(nameof(MemberItem));
}
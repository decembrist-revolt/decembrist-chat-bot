using System.Linq.Expressions;
using DecembristChatBotSharp.Entity;
using LiteDB;

namespace DecembristChatBotSharp;

public class Database(AppConfig appConfig)
{
    private readonly LiteDatabase _db = new(appConfig.DatabaseFile);

    public Unit AddNewMember(long telegramId, string username, long chatId, int welcomeMessageId)
    {
        var newMembers = _db.GetCollection<NewMember>(nameof(NewMember));
        newMembers.Insert(new NewMember(telegramId, username, chatId, welcomeMessageId, DateTime.UtcNow));
        return unit;
    }

    public IEnumerable<NewMember> GetNewMembers(DateTime olderThan)
    {
        var newMembers = _db.GetCollection<NewMember>(nameof(NewMember));
        return newMembers.Find(member => member.EnterDate < olderThan);
    }
    
    public Option<NewMember> GetNewMember(long telegramId, long chatId)
    {
        var newMembers = _db.GetCollection<NewMember>(nameof(NewMember));
        return Optional(
            newMembers.FindOne(ChatUserPredicate(chatId, telegramId)));
    }

    public bool RemoveNewMember(long telegramId, long chatId)
    {
        var newMembers = _db.GetCollection<NewMember>(nameof(NewMember));
        var deleteCount = newMembers.DeleteMany(ChatUserPredicate(chatId, telegramId));
        return deleteCount == 1;
    }

    /// <summary>
    /// Predicate for selecting a new member by chat and telegram id
    /// </summary>
    private Expression<Func<NewMember, bool>> ChatUserPredicate(long chatId, long telegramId) =>
        member => member.TelegramId == telegramId && member.ChatId == chatId;
}
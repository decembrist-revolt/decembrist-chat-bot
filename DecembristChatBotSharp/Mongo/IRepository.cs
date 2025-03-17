namespace DecembristChatBotSharp.Mongo;

public interface IRepository
{
    public Task<Unit> EnsureIndexes() => Task.FromResult(unit);
}
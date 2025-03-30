using MongoDB.Driver;

namespace DecembristChatBotSharp.Mongo;

public class EmptyAsyncCursor<T> : IAsyncCursor<T>
{
    public void Dispose()
    {
    }

    public bool MoveNext(CancellationToken cancellationToken = default) => false;

    public Task<bool> MoveNextAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public IEnumerable<T> Current { get; } = [];
}
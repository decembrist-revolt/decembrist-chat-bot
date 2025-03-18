using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp;

public static class UtilsExtensions
{
    public static Task<Unit> UnitTask(this Task task) => task.ContinueWith(_ => unit);

    public static Unit Ignore(this object any) => unit;

    public static Task<Unit> SendMessageAndLog(
        this BotClient botClient,
        long chatId,
        string message,
        Action<Message> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken) =>
        botClient.SendMessage(chatId, message, cancellationToken: cancelToken).ToTryAsync().Match(onSent, onError);
    
    public static Task<Unit> DeleteMessageAndLog(
        this BotClient botClient,
        long chatId,
        int messageId,
        Action onDeleted,
        Action<Exception> onError) =>
        botClient.DeleteMessage(chatId, messageId).ToTryAsync().Match(_ => onDeleted(), onError);

    public static TryAsync<T> ToTryAsync<T>(this Task<T> task) => TryAsync(task);
    
    public static TryAsync<Unit> ToTryAsync(this Task task) => TryAsync(async () =>
    {
        await task;
        return unit;
    });

    public static TryOptionAsync<T> ToTryOption<T>(this Task<T> task) => TryOptionAsync(task);
    
    
    public static Task<Unit> WhenAll(this IEnumerable<Task> tasks) => Task.WhenAll(tasks).UnitTask();
    
    public static Task<T[]> AwaitAll<T>(this IEnumerable<Task<T>> tasks) => Task.WhenAll(tasks);

    public static string GetUsername(this ChatMember member, bool tag = true) => Optional(member.User.Username)
        .Map(username => tag ? $"@{username}" : username)
        .IfNone($"{member.User.FirstName} {member.User.LastName}");
}
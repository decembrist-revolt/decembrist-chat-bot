using System.Text.RegularExpressions;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using JasperFx.Core;
using LanguageExt.UnsafeValueAccess;
using MongoDB.Driver;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp;

public static class UtilsExtensions
{
    private const string MeLink = "https://t.me";

    public static Task<Unit> UnitTask(this Task? task) => task?.ContinueWith(_ => unit) ?? Task.FromResult(unit);

    public static Unit Ignore(this object any) => unit;


    public static Task<Unit> SendMessageAndLog(
        this BotClient botClient,
        long chatId,
        string message,
        Action<Message> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken) =>
        botClient.SendMessage(chatId, message, cancellationToken: cancelToken).ToTryAsync().Match(onSent, onError);


    public static Task<Unit> SendMessageAndLog(
        this BotClient botClient,
        long chatId,
        string message,
        ParseMode parseMode,
        Action<Message> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken) =>
        botClient.SendMessage(chatId, message, parseMode: parseMode, cancellationToken: cancelToken)
            .ToTryAsync()
            .Match(onSent, onError);

    public static Task<Unit> SendMessageAndLog(
        this BotClient botClient,
        long chatId,
        string message,
        int replyMessageId,
        Action<Message> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken) => botClient.SendMessage(
            chatId,
            message,
            replyParameters: new ReplyParameters { MessageId = replyMessageId },
            cancellationToken: cancelToken)
        .ToTryAsync()
        .Match(onSent, onError);

    public static Task<Unit> SendMessageAndLog(
        this BotClient botClient,
        long chatId,
        string message,
        Action<Message> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken,
        ReplyMarkup replyMarkup,
        ParseMode parseMode = ParseMode.None) =>
        botClient.SendMessage(chatId, message, parseMode: parseMode, replyMarkup: replyMarkup,
                cancellationToken: cancelToken)
            .ToTryAsync()
            .Match(onSent, onError);

    public static Task<Unit> EditMessageAndLog(
        this BotClient botClient,
        long chatId,
        int messageId,
        string message,
        Action<Message> onEdit,
        Action<Exception> onError,
        CancellationToken cancelToken,
        ParseMode parseMode = ParseMode.None,
        InlineKeyboardMarkup? replyMarkup = null) =>
        botClient.EditMessageText(chatId, messageId, message,
                replyMarkup: replyMarkup, parseMode: parseMode, cancellationToken: cancelToken)
            .ToTryAsync()
            .Match(onEdit, onError);

    public static Task<Unit> DeleteMessageAndLog(
        this BotClient botClient,
        long chatId,
        int messageId,
        Action onDeleted,
        Action<Exception> onError,
        CancellationToken cancelToken) =>
        botClient
            .DeleteMessage(chatId, messageId, cancellationToken: cancelToken)
            .ToTryAsync()
            .Match(_ => onDeleted(), onError);

    public static Task<Unit> SetReactionAndLog(
        this BotClient botClient,
        long chatId,
        int messageId,
        IEnumerable<ReactionTypeEmoji> emojis,
        Action<Unit> onSent,
        Action<Exception> onError,
        CancellationToken cancelToken) =>
        botClient.SetMessageReaction(chatId, messageId, emojis, cancellationToken: cancelToken).ToTryAsync()
            .Match(onSent, onError);

    public static TryAsync<T> ToTryAsync<T>(this Task<T> task) => TryAsync(task);

    public static TryAsync<Unit> ToTryAsync(this Task task) => TryAsync(async () =>
    {
        await task;
        return unit;
    });

    public static TryAsync<T> Ensure<T>(this TryAsync<T> tryAsync,
        Func<T, bool> predicate,
        Func<T, Exception> exceptionFactory) =>
        tryAsync.Bind(value => predicate(value) ? TryAsyncSucc(value) : TryAsyncFail<T>(exceptionFactory(value)));

    public static TryAsync<T> Ensure<T>(this TryAsync<T> tryAsync,
        Func<T, bool> predicate,
        Func<T, string> exceptionMessageFactory) =>
        tryAsync.Ensure(predicate, value => new Exception(exceptionMessageFactory(value)));
    
    public static TryAsync<T> Ensure<T>(this TryAsync<T> tryAsync, Func<T, bool> predicate, Exception exception) => 
        tryAsync.Ensure(predicate, _ => exception);
    
    public static TryAsync<T> Ensure<T>(this TryAsync<T> tryAsync, Func<T, bool> predicate, string exceptionMessage) => 
        tryAsync.Ensure(predicate, _ => new Exception(exceptionMessage));

    public static TryOptionAsync<T> ToTryOption<T>(this Task<T> task) => TryOptionAsync(task);
    
    /// <summary>
    /// Throws an exception if the given Option is None; otherwise, returns the value.
    /// </summary>
    /// <typeparam name="T">The type of the value contained in the Option.</typeparam>
    /// <param name="option">The Option to evaluate.</param>
    /// <returns>The value contained in the Option if it is Some.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the Option is None.</exception>
    public static T IfNoneThrow<T>(this Option<T> option) => 
        option.IsSome ? option.ValueUnsafe() : throw new InvalidOperationException("Option is None");

    /// <summary>
    /// Throws an exception if the given Either is Left; otherwise, returns the Right value.
    /// </summary>
    /// <typeparam name="T">The type of the Left value.</typeparam>
    /// <typeparam name="TR">The type of the Right value.</typeparam>
    /// <param name="either">The Either to evaluate.</param>
    /// <returns>The Right value if the Either is Right.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the Either is Left.</exception>
    public static TR IfLeftThrow<T, TR>(this Either<T, TR> either) =>
        either.IsRight ? either.ValueUnsafe() : throw new InvalidOperationException("Either is Left");
    
    /// <summary>
    /// Throws an exception if the given Either is Right; otherwise, returns the Left value.
    /// /// </summary>
    /// <typeparam name="T">The type of the Left value.</typeparam>
    /// <typeparam name="TR">The type of the Right value.</typeparam>
    /// <param name="either">The Either to evaluate.</param>
    /// <returns>The Left value if the Either is Left.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the Either is Right.</exception>
    public static T IfRightThrow<T, TR>(this Either<T, TR> either) =>
        either.IsLeft ? either.Swap().ValueUnsafe() : throw new InvalidOperationException("Either is Right");

    public static Task<Unit> WhenAll(this IEnumerable<Task> tasks) => Task.WhenAll(tasks).UnitTask();

    public static Task<T[]> AwaitAll<T>(this IEnumerable<Task<T>> tasks) => Task.WhenAll(tasks);

    public static string GetUsername(this ChatMember member, bool tag = true) => Optional(member.User.Username)
        .Map(username => tag ? $"@{username}" : username)
        .IfNone($"{member.User.FirstName} {member.User.LastName}");

    public static string EscapeMarkdown(this string text) =>
        Regex.Replace(text, @"([\\_\*\[\]\(\)\~\`\>\#\+\-\=\|\{\}\.\!])", @"\$1");

    public static async Task<Option<string>> GetUsername(
        this BotClient botClient,
        long chatId,
        long telegramId,
        CancellationToken cancelToken) => await botClient.GetChatMember(chatId, telegramId, cancelToken)
        .ToTryAsync()
        .Map(member => member.GetUsername())
        .Match(Optional, ex =>
        {
            Log.Error(ex, "Failed to get chat member in chat {0} with telegramId {1}", chatId, telegramId);
            return None;
        });

    public static async Task<string> GetUsernameOrId(
        this BotClient botClient, long telegramId, long chatId, CancellationToken cancelToken) =>
        await botClient.GetUsername(chatId, telegramId, cancelToken)
            .ToAsync()
            .IfNone(telegramId.ToString);

    public static async Task<string> GetChatTitleOrId(this BotClient botClient,
        long chatId,
        CancellationToken cancelToken) =>
        await botClient.GetChatTitle(chatId, cancelToken)
            .ToAsync()
            .IfNone(chatId.ToString);

    public static async Task<Option<string>> GetChatTitle(
        this BotClient botClient,
        long chatId,
        CancellationToken cancelToken) => await botClient.GetChat(chatId, cancelToken)
        .ToTryAsync()
        .Map(chat => chat.Title)
        .Match(Optional, ex =>
        {
            Log.Error(ex, "Failed to get chat title for chat {0}", chatId);
            return None;
        });

    public static async Task<string> GetBotStartLink(this BotClient botClient, string parameters) =>
        await botClient.GetMe()
            .ToTryOption()
            .Map(me => me.Username)
            .Filter(username => username?.IsNotEmpty() == true)
            .Map(botName => $"{MeLink}/{botName}?start={Uri.EscapeDataString(parameters)}")
            .Match(identity, () => MeLink, ex =>
            {
                Log.Error(ex, "Failed get self bot username");
                return MeLink;
            });

    public static Task<bool> TryCommit(this IClientSessionHandle session, CancellationToken cancelToken) =>
        session.CommitTransactionAsync(cancelToken).ToTryAsync().Match(_ => true, ex =>
        {
            Log.Error(ex, "Failed to commit transaction");
            return false;
        });

    public static Task<bool> TryAbort(this IClientSessionHandle session, CancellationToken cancelToken) =>
        session.AbortTransactionAsync(cancelToken).ToTryAsync().Match(_ => true, ex =>
        {
            Log.Error(ex, "Failed to abort transaction");
            return false;
        });

    public static BotCommand[] GetCommandsByLevel(this IEnumerable<ICommandHandler> handlers, CommandLevel level) =>
        handlers.Where(handler => handler.CommandLevel != CommandLevel.None)
            .Where(handler => level.HasFlag(handler.CommandLevel))
            .Select(handler => new BotCommand(handler.Command, handler.Description))
            .OrderBy(command => command.Command)
            .ToArray();
}
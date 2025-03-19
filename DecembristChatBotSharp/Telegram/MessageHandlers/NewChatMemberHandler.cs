using DecembristChatBotSharp.Mongo;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public readonly struct NewMemberHandlerParams(
    long chatId,
    User user
)
{
    public long ChatId => chatId;
    public User User => user;
}

internal readonly struct UsernameEx(string username, Exception ex)
{
    public string Username => username;
    public Exception Ex => ex;
}

public class NewMemberHandler(
    AppConfig appConfig,
    BotClient botClient,
    NewMemberRepository db,
    WhiteListRepository whiteListDb,
    CancellationTokenSource cancelToken
)
{
    public async Task<Unit> Do(NewMemberHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var user = parameters.User;
        var telegramId = user.Id;

        if (await whiteListDb.IsWhiteListMember(telegramId))
            return unit;

        var sendWelcomeTask = await SendWelcomeMessageForUser(chatId, user);

        return sendWelcomeTask.Match(
            Right: username => Log.Information("Sent welcome message to {Username}", username),
            Left: usernameEx =>
                Log.Error(usernameEx.Ex, "Failed to send welcome message to {Username}", usernameEx.Username)
        );
    }

    private async Task<Either<UsernameEx, string>> SendWelcomeMessageForUser(long chatId, User user)
    {
        var username = Optional(user.Username).Match(
            Some: username => $"@{username}",
            None: () => user.FirstName
        );

        var welcomeText = string.Format(appConfig.WelcomeMessage, username, appConfig.CaptchaTimeSeconds);
        var trySend = TryAsync(
            botClient.SendMessage(chatId: chatId, text: welcomeText, cancellationToken: cancelToken.Token));

        return await trySend
            .Bind(message => db.AddNewMember(user.Id, username, chatId, message.MessageId))
            .ToEither()
            .BiMap(
                _ => username,
                ex => new UsernameEx(username, ex)
            );
    }
}
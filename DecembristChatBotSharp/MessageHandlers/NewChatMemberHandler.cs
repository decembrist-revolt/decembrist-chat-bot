using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.MessageHandlers;

public readonly struct NewMemberHandlerParams(
    long chatId,
    User[] users,
    AppConfig appConfig,
    BotClient botClient,
    Database db
)
{
    public long ChatId => chatId;
    public User[] Users => users;
    public AppConfig AppConfig => appConfig;
    public BotClient BotClient => botClient;
    public Database Db => db;
}

internal record UsernameEx(string Username, Exception Ex);

public class NewMemberHandler(
    AppConfig appConfig,
    BotClient botClient,
    Database db)
{
    public async Task<Unit> Do(
        NewMemberHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var chatId = parameters.ChatId;
        var users = parameters.Users;

        var sendTasks = users.Map(user => SendWelcomeMessageForUser(chatId, user, cancelToken));
        var sendWelcomeTasks = await Task.WhenAll(sendTasks);
        
        return sendWelcomeTasks.Iter(either => either.Match(
            Right: username => Log.Information("Sent welcome message to {Username}", username),
            Left: usernameEx =>
                Log.Error(usernameEx.Ex, "Failed to send welcome message to {Username}", usernameEx.Username)
        ));
    }

    private async Task<Either<UsernameEx, string>> SendWelcomeMessageForUser(
        long chatId,
        User user, 
        CancellationToken cancelToken)
    {
        var username = Optional(user.Username).Match(
            Some: username => $"@{username}",
            None: () => user.FirstName
        );

        var welcomeText = string.Format(appConfig.WelcomeMessage, username, appConfig.CaptchaTimeSeconds);
        var result =
            TryAsync(botClient.SendMessage(chatId: chatId, text: welcomeText, cancellationToken: cancelToken));
        
        return await result
            .Bind(message => TryAsync(db.AddNewMember(user.Id, username, chatId, message.MessageId)))
            .Match<Unit, Either<UsernameEx, string>>(
                Succ: _ => Right(username),
                Fail: ex => Left(new UsernameEx(username, ex)));
    }
}
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.MessageHandlers;

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
    Database db)
{
    public async Task<Unit> Do(
        NewMemberHandlerParams parameters,
        CancellationToken cancelToken)
    {
        var chatId = parameters.ChatId;
        var user = parameters.User;
        var telegramId = user.Id;

        if (appConfig.WhiteListIds?.Contains(telegramId) == true) return unit;

        var sendWelcomeTask = await SendWelcomeMessageForUser(chatId, user, cancelToken);

        return sendWelcomeTask.Match(
            Right: username => Log.Information("Sent welcome message to {Username}", username),
            Left: usernameEx =>
                Log.Error(usernameEx.Ex, "Failed to send welcome message to {Username}", usernameEx.Username)
        );
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
        var result = await TryAsync(
            botClient.SendMessage(chatId: chatId, text: welcomeText, cancellationToken: cancelToken));

        return result
            .Map(message => db.AddNewMember(user.Id, username, chatId, message.MessageId))
            .Match<Unit, Either<UsernameEx, string>>(
                Succ: _ => Right(username),
                Fail: ex => Left(new UsernameEx(username, ex)));
    }
}
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
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

[Singleton]
public class NewMemberHandler(
    BotClient botClient,
    NewMemberRepository newMemberRepository,
    WhiteListRepository whiteListRepository,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    CancellationTokenSource cancelToken
)
{
    public async Task<Unit> Do(NewMemberHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var user = parameters.User;
        var telegramId = user.Id;

        if (await whiteListRepository.IsWhiteListMember((telegramId, chatId)))
        {
            Log.Information("Whitelist member {0} joined", telegramId);
            return unit;
        }

        var maybeCaptchaConfig = await chatConfigService.GetConfig(chatId, config => config.CaptchaConfig);
        if (!maybeCaptchaConfig.TryGetSome(out var captchaConfig))
        {
            return unit;
        }

        var sendWelcomeTask = await SendWelcomeMessageForUser(chatId, user, captchaConfig);

        return sendWelcomeTask.Match(
            username => Log.Information("Sent welcome message to {Username}", username),
            usernameEx =>
                Log.Error(usernameEx.Ex, "Failed to send welcome message to {Username}", usernameEx.Username)
        );
    }

    private async Task<Either<UsernameEx, string>> SendWelcomeMessageForUser(long chatId, User user, CaptchaConfig2 captchaConfig)
    {
        var username = Optional(user.Username).Match(
            Some: username => $"@{username}",
            None: () => user.FirstName
        );

        var welcomeText = string.Format(
            captchaConfig.WelcomeMessage, username, captchaConfig.CaptchaAnswer);
        var trySend = TryAsync(
            botClient.SendMessage(chatId: chatId, text: welcomeText, cancellationToken: cancelToken.Token));

        return await trySend
            .Bind(message =>
            {
                var expireAt = DateTime.UtcNow.AddMinutes(captchaConfig.WelcomeMessageExpiration);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);
                return newMemberRepository.AddNewMember(user.Id, username, chatId, message.MessageId);
            })
            .ToEither()
            .BiMap(
                _ => username,
                ex => new UsernameEx(username, ex)
            );
    }
}
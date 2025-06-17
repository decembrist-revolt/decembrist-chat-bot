using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

public enum Result
{
    JustMessage,
    Captcha
}

[Singleton]
public class CaptchaHandler(
    AppConfig appConfig,
    BotClient botClient,
    BanService banService,
    NewMemberRepository newMemberRepository,
    WhiteListRepository whiteListRepository,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken
)
{
    private readonly CaptchaConfig _captchaConfig = appConfig.CaptchaConfig;

    public async Task<Result> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        var payload = parameters.Payload;

        var maybe = await newMemberRepository.FindNewMember((telegramId, chatId)).Match(
            identity,
            ex => OnNewMemberNotFound(ex, telegramId, chatId));
        if (maybe.IsNone) return Result.JustMessage;

        var newMember = maybe.ValueUnsafe();

        var tryCaptcha = TryAsync(IsCaptchaPassed(payload)
            ? OnCaptchaPassed(telegramId, chatId, messageId, newMember)
            : OnCaptchaFailed(chatId, messageId, newMember));

        return await tryCaptcha.IfFail(ex =>
                Log.Error(ex, "Captcha handler failed for user {0} in chat {1}", telegramId, chatId))
            .Map(_ => Result.Captcha);
    }

    private OptionNone OnNewMemberNotFound(Error error, long telegramId, long chatId)
    {
        Log.Error(error, "New member not found for user {0} in chat {1}", telegramId, chatId);
        return None;
    }

    private bool IsCaptchaPassed(IMessagePayload payload) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(appConfig.CaptchaConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);

    private async Task<Unit> OnCaptchaPassed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember)
    {
        var joinMessage = string.Format(_captchaConfig.JoinText, newMember.Username);
        return await newMemberRepository.RemoveNewMember((telegramId, chatId))
            .MapAsync(_ => Array(
                whiteListRepository.AddWhiteListMember(new WhiteListMember((telegramId, chatId))),
                botClient.DeleteMessages(chatId, [messageId, newMember.WelcomeMessageId], cancelToken.Token),
                botClient.SendMessage(chatId, joinMessage, cancellationToken: cancelToken.Token)
            ).WhenAll());
    }

    private async Task<Unit> OnCaptchaFailed(long chatId, int messageId, NewMember newMember)
    {
        Log.Information("User {0} failed captcha in chat {1}", newMember.Id.TelegramId, chatId);
        var retryCount = newMember.CaptchaRetryCount;

        var captchaTask = retryCount switch
        {
            _ when retryCount >= _captchaConfig.CaptchaRetryCount => KickCaptchaFailedUser(chatId, newMember),
            _ when retryCount % _captchaConfig.CaptchaRequestAgainCount == 0.0 => SendCaptchaMessage(chatId, newMember),
            _ => newMemberRepository.AddMemberItem(newMember with { CaptchaRetryCount = retryCount + 1 }).ToUnit()
        };
        return await Array(captchaTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(CaptchaHandler))).WhenAll();
    }

    private async Task<Unit> KickCaptchaFailedUser(long chatId, NewMember newMember) =>
        await Array(banService.KickChatMember(chatId, newMember.Id.TelegramId),
            messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(CaptchaHandler)),
            newMemberRepository.RemoveNewMember(newMember.Id).ToUnit()
        ).WhenAll();

    private async Task<Unit> SendCaptchaMessage(long chatId, NewMember newMember)
    {
        var username = newMember.Username;
        var text = string.Format(_captchaConfig.CaptchaRequestAgainText, username, _captchaConfig.CaptchaAnswer);
        await messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(CaptchaHandler));

        return await botClient.SendMessage(chatId, text, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async message =>
                {
                    await newMemberRepository.AddMemberItem(newMember with
                    {
                        CaptchaRetryCount = newMember.CaptchaRetryCount + 1, WelcomeMessageId = message.MessageId
                    });
                    Log.Information("Sent captcha message to chat {0}", chatId);
                    return unit;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to send captcha message to chat {0}", chatId);
                    return unit;
                });
    }
}
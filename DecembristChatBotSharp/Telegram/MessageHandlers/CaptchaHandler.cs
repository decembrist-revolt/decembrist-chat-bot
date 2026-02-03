using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService
)
{
    private readonly CaptchaJobConfig _captchaJobConfig = appConfig.CaptchaJobConfig;

    public async Task<Result> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeConfig = chatConfigService.GetConfig(parameters.ChatConfig, config => config.CaptchaConfig);
        if (!maybeConfig.TryGetSome(out var captchaConfig))
        {
            return chatConfigService.LogNonExistConfig(Result.JustMessage, nameof(Entity.Configs.CaptchaConfig));
        }

        var payload = parameters.Payload;

        var maybe = await newMemberRepository.FindNewMember((telegramId, chatId)).Match(
            identity,
            ex => OnNewMemberNotFound(ex, telegramId, chatId));
        if (maybe.IsNone) return Result.JustMessage;

        var newMember = maybe.ValueUnsafe();

        var tryCaptcha = TryAsync(IsCaptchaPassed(payload, captchaConfig)
            ? OnCaptchaPassed(telegramId, chatId, messageId, newMember, captchaConfig)
            : OnCaptchaFailed(chatId, messageId, newMember, captchaConfig));

        return await tryCaptcha.IfFail(ex =>
                Log.Error(ex, "Captcha handler failed for user {0} in chat {1}", telegramId, chatId))
            .Map(_ => Result.Captcha);
    }

    private OptionNone OnNewMemberNotFound(Error error, long telegramId, long chatId)
    {
        Log.Error(error, "New member not found for user {0} in chat {1}", telegramId, chatId);
        return None;
    }

    private bool IsCaptchaPassed(IMessagePayload payload, CaptchaConfig captchaConfig) =>
        payload is TextPayload { Text: var text } &&
        string.Equals(captchaConfig.CaptchaAnswer, text, StringComparison.CurrentCultureIgnoreCase);

    private async Task<Unit> OnCaptchaPassed(
        long telegramId,
        long chatId,
        int messageId,
        NewMember newMember,
        CaptchaConfig captchaConfig)
    {
        var joinMessage = string.Format(captchaConfig.JoinText, newMember.Username);
        return await newMemberRepository.RemoveNewMember((telegramId, chatId))
            .MapAsync(_ => Array(
                whiteListRepository.AddWhiteListMember(new WhiteListMember((telegramId, chatId))),
                botClient.DeleteMessages(chatId, [messageId, newMember.WelcomeMessageId], cancelToken.Token),
                botClient.SendMessage(chatId, joinMessage, cancellationToken: cancelToken.Token)
            ).WhenAll());
    }

    private async Task<Unit> OnCaptchaFailed(
        long chatId, int messageId, NewMember newMember, CaptchaConfig captchaConfig)
    {
        Log.Information("User {0} failed captcha in chat {1}", newMember.Id.TelegramId, chatId);
        var retryCount = newMember.CaptchaRetryCount;

        var captchaTask = retryCount switch
        {
            _ when retryCount >= _captchaJobConfig.CaptchaRetryCount => KickCaptchaFailedUser(chatId, newMember),
            _ when retryCount % _captchaJobConfig.CaptchaRequestAgainCount == 0 => SendCaptchaMessage(chatId, newMember,
                captchaConfig),
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

    private async Task<Unit> SendCaptchaMessage(long chatId, NewMember newMember, CaptchaConfig captchaConfig)
    {
        var username = newMember.Username;
        var text = string.Format(captchaConfig.CaptchaRequestAgainText, username, captchaConfig.CaptchaAnswer);
        await messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(CaptchaHandler));

        return await botClient.SendMessage(chatId, text, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async message =>
                {
                    var expireAt = DateTime.UtcNow.AddMinutes(captchaConfig.CaptchaRequestAgainExpiration);
                    expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);
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
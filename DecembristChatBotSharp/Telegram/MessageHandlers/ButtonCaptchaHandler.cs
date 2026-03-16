using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Service.Buttons;
using Lamar;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class ButtonCaptchaHandler(
    AppConfig appConfig,
    BotClient botClient,
    BanService banService,
    NewMemberRepository newMemberRepository,
    ExpiredMessageRepository expiredMessageRepository,
    MessageAssistance messageAssistance,
    CaptchaButtons captchaButtons,
    CallbackRepository callbackRepository,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService
)
{
    private readonly CaptchaJobConfig _captchaJobConfig = appConfig.CaptchaJobConfig;

    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.CaptchaConfig);
        if (!maybeConfig.TryGetSome(out var captchaConfig))
            return chatConfigService.LogNonExistConfig(false, nameof(CaptchaConfig));

        var maybe = await newMemberRepository.FindNewMember((telegramId, chatId)).Match(
            identity,
            ex => OnNewMemberNotFound(ex, telegramId, chatId));

        if (maybe.IsNone) return false;

        var newMember = maybe.ValueUnsafe();

        await OnMessageFromNewMember(chatId, messageId, newMember, captchaConfig);

        return true;
    }

    private static Option<NewMember> OnNewMemberNotFound(Error error, long telegramId, long chatId)
    {
        Log.Error(error, "New member not found for user {0} in chat {1}", telegramId, chatId);
        return None;
    }

    private async Task<Unit> OnMessageFromNewMember(
        long chatId, int messageId, NewMember newMember, CaptchaConfig captchaConfig)
    {
        Log.Information("ButtonCaptchaHandler: user {0} wrote in chat {1} before passing captcha",
            newMember.Id.TelegramId, chatId);

        var retryCount = newMember.CaptchaRetryCount;

        var captchaTask = retryCount switch
        {
            _ when retryCount >= _captchaJobConfig.CaptchaRetryCount => KickCaptchaFailedUser(chatId, newMember),
            _ when retryCount % _captchaJobConfig.CaptchaRequestAgainCount == 0 => ResendCaptchaButtons(chatId,
                newMember, captchaConfig),
            _ => newMemberRepository.AddMemberItem(newMember with { CaptchaRetryCount = retryCount + 1 }).ToUnit()
        };

        return await Array(
            captchaTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, nameof(ButtonCaptchaHandler))
        ).WhenAll();
    }

    private async Task<Unit> KickCaptchaFailedUser(long chatId, NewMember newMember) =>
        await Array(
            banService.KickChatMember(chatId, newMember.Id.TelegramId),
            messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(ButtonCaptchaHandler)),
            newMemberRepository.RemoveNewMember(newMember.Id).ToUnit()
        ).WhenAll();

    private async Task<Unit> ResendCaptchaButtons(long chatId, NewMember newMember, CaptchaConfig captchaConfig)
    {
        var telegramId = newMember.Id.TelegramId;
        var username = newMember.Username;
        var text = string.Format(captchaConfig.CaptchaRequestAgainText, username, captchaConfig.CaptchaAnswer);
        var replyMarkup = captchaButtons.GetMarkup(telegramId, captchaConfig);

        await messageAssistance.DeleteCommandMessage(chatId, newMember.WelcomeMessageId, nameof(ButtonCaptchaHandler));

        return await botClient
            .SendMessage(chatId, text, replyMarkup: replyMarkup, cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                async message =>
                {
                    var expireAt = DateTime.UtcNow.AddMinutes(captchaConfig.CaptchaRequestAgainExpiration);
                    expiredMessageRepository.QueueMessage(chatId, message.MessageId, expireAt);

                    var permission = new CallbackPermission(
                        new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.Captcha, message.MessageId),
                        expireAt);
                    await callbackRepository.AddCallbackPermission(permission);

                    await newMemberRepository.AddMemberItem(newMember with
                    {
                        CaptchaRetryCount = newMember.CaptchaRetryCount + 1,
                        WelcomeMessageId = message.MessageId
                    });

                    Log.Information("ButtonCaptchaHandler: resent captcha buttons to chat {0}", chatId);
                    return unit;
                },
                ex =>
                {
                    Log.Error(ex, "ButtonCaptchaHandler: failed to resend captcha buttons to chat {0}", chatId);
                    return unit;
                });
    }
}
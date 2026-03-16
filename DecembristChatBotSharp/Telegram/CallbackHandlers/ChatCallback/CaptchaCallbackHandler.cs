using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;

[Singleton]
public class CaptchaCallbackHandler(
    NewMemberRepository newMemberRepository,
    WhiteListRepository whiteListRepository,
    MessageAssistance messageAssistance,
    ChatConfigService chatConfigService,
    CallbackRepository callbackRepository,
    CancellationTokenSource cancelToken,
    BanService banService) : IChatCallbackHandler
{
    public const string PrefixKey = "Captcha";

    public string Prefix => PrefixKey;

    public async Task<Unit> Do(CallbackQueryParameters queryParameters)
    {
        var (_, suffix, chatId, telegramId, messageId, queryId, maybeParameters) = queryParameters;
        var id = new CallbackPermission.CompositeId(chatId, telegramId, CallbackType.Captcha, messageId);

        if (!await callbackRepository.HasPermission(id)) return await SendNotAccess(chatId, queryId);

        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.CaptchaConfig);
        if (!maybeConfig.TryGetSome(out var captchaConfig))
        {
            return chatConfigService.LogNonExistConfig(unit, nameof(CaptchaConfig), nameof(CaptchaCallbackHandler));
        }

        var maybeMember = await newMemberRepository
            .FindNewMember(new NewMember.CompositeId(telegramId, chatId))
            .Match(identity, ex =>
            {
                Log.Error(ex, "Failed to find new member {0} in chat {1}", telegramId, chatId);
                return Option<NewMember>.None;
            });

        if (!maybeMember.TryGetSome(out var newMember)) return unit;

        await messageAssistance.DeleteCommandMessage(chatId, messageId, PrefixKey);

        if (string.Equals(suffix, captchaConfig.CaptchaAnswer, StringComparison.OrdinalIgnoreCase))
        {
            return await HandleCorrect(chatId, telegramId, newMember, captchaConfig);
        }

        return await HandleWrongAnswer(chatId, telegramId, queryId);
    }

    private async Task<Unit> HandleCorrect(long chatId, long telegramId, NewMember newMember,
        CaptchaConfig captchaConfig)
    {
        Log.Information("User {0} passed captcha in chat {1}", telegramId, chatId);
        var joinMessage = string.Format(captchaConfig.JoinText, newMember.Username);

        await newMemberRepository.RemoveNewMember(newMember.Id);
        return await Array(
            whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId))).ToUnit(),
            messageAssistance.SendMessageExpired(chatId, joinMessage, PrefixKey)
        ).WhenAll();
    }

    private Task<Unit> HandleWrongAnswer(long chatId, long telegramId, string queryId)
    {
        Log.Information("User {0} failed captcha in chat {1}, user kicked", telegramId, chatId);
        return banService.KickChatMember(chatId, telegramId);
    }

    private async Task<Unit> SendNotAccess(long chatId, string queryId)
    {
        var message = "Это сообщение для проходящего капчу";
        return await messageAssistance.AnswerCallbackQuery(queryId, chatId, Prefix, message);
    }
}
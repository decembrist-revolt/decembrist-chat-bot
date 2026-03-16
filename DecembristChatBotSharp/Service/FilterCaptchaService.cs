using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Serilog;

namespace DecembristChatBotSharp.Service;

public class FilterCaptchaService(
    AppConfig appConfig,
    WhiteListRepository whiteListRepository,
    FilterRecordRepository filterRecordRepository,
    AdminUserRepository adminUserRepository,
    DeepSeekService deepSeekService)
{
    private const int MaxMessageLength = 6;

    private static readonly Regex LinkRegex = new(@"([^\s<>]+\.[^\s<>]{2,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _scamTraitors =
        "Признаки скама, достаточно одного:\n - " + string.Join("\n - ", appConfig.FilterJobConfig.ScamTraitors) + "\n";

    private const string ModeratorPrompt = """
                                           Ты - модератор. Ты определяешь, является ли сообщение спамом, мошенничеством (скамом) или рекламой,
                                           Ответь СТРОГО в формате JSON, без дополнительного текста:
                                           если скам: {"isScam": true}, иначе: {"isScam": false}
                                           """;

    public async Task<bool> IsSuspectMessage(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (await whiteListRepository.IsWhiteListMember((telegramId, chatId)) ||
            await adminUserRepository.IsAdmin((telegramId, chatId))) return false;

        if (parameters.Payload is not TextPayload { IsLink: var isLink, Text: var text })
            return true;
        if (isLink || LinkRegex.IsMatch(text) || !await IsFiltered(text, chatId))
            return true;

        return await IsSuspectFromAiModeration(chatId, telegramId, messageId, text, parameters.ReplyToMessageText);
    }

    private async Task<bool> IsSuspectFromAiModeration(long chatId, long telegramId, int messageId, string text,
        Option<string> maybeReplyText)
    {
        var messageToCheck = $"Сообщение для проверки:\n\"{text}\"";
        if (maybeReplyText.TryGetSome(out var replyText))
        {
            messageToCheck = $"""
                              Ответ на сообщение:
                              "{replyText}"
                              {messageToCheck}
                              """;
        }

        var prompt = ModeratorPrompt + _scamTraitors + messageToCheck;

        var maybeVerdict = await deepSeekService.GetModerateVerdict(prompt, chatId, telegramId);
        if (!maybeVerdict.TryGetSome(out var isScam))
        {
            Log.Error("Ai Moderation is fail, no action to user");
            return true;
        }

        if (isScam) return true;

        await whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId)));
        return false;
    }

    private async Task<bool> IsFiltered(string text, long chatId)
    {
        if (text.Length > MaxMessageLength) return true;
        return await filterRecordRepository.IsFilterRecordContain(chatId, text);
    }
}
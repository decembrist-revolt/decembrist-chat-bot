using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilteredMessageHandler(
    WhiteListRepository whiteListRepository,
    FilterRecordRepository filterRecordRepository,
    FilteredMessageRepository filteredMessageRepository,
    DeepSeekService deepSeekService,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken,
    ChatConfigService chatConfigService)
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


    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        if (await whiteListRepository.IsWhiteListMember((telegramId, chatId))) return false;
        if (parameters.Payload is StickerPayload) return await SendCaptchaMessage(chatId, messageId, telegramId);
        if (parameters.Payload is not TextPayload { IsLink: var isLink, Text: var text }) return false;

        if (isLink || LinkRegex.IsMatch(text) || !await IsFiltered(text, chatId))
            return await SendCaptchaMessage(chatId, messageId, telegramId);
        return await CheckAiModeration(chatId, telegramId, messageId, text, parameters.ReplyToMessageText);
    }

    private async Task<bool> SendCaptchaMessage(long chatId, int messageId, long telegramId)
    {
        var maybeFilterConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeFilterConfig.TryGetSome(out var filterConfig))
        {
            return chatConfigService.LogNonExistConfig(false, nameof(FilterConfig),
                nameof(FilteredMessageHandler));
        }

        var messageText = string.Format(
            filterConfig.CaptchaMessage, filterConfig.CaptchaAnswer, filterConfig.CaptchaTimeSeconds);
        return await botClient.SendMessage(chatId, messageText,
                replyParameters: new ReplyParameters { MessageId = messageId },
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async m =>
                {
                    var message = new FilteredMessage((chatId, messageId), telegramId, m.MessageId, DateTime.UtcNow);
                    await filteredMessageRepository.AddFilteredMessage(message);

                    Log.Information("Success create filtered message {0}, author: {1}", message.Id, telegramId);
                    return true;
                },
                ex =>
                {
                    Log.Error(ex, "Failed to create filtered message in chat {0}, author: {1}", chatId, telegramId);
                    return false;
                });
    }

    private async Task<bool> CheckAiModeration(long chatId, long telegramId, int messageId, string text,
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
            return await SendCaptchaMessage(chatId, messageId, telegramId);
        }

        if (isScam) return await SendCaptchaMessage(chatId, messageId, telegramId);

        await whiteListRepository.AddWhiteListMember(new WhiteListMember(new CompositeId(telegramId, chatId)));
        return false;
    }

    private async Task<bool> IsFiltered(string text, long chatId)
    {
        if (text.Length > MaxMessageLength) return true;
        return await filterRecordRepository.IsFilterRecordContain(chatId, text);
    }
}
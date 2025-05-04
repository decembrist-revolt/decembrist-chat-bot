using System.Text.RegularExpressions;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.LorHandler;

[Singleton]
public class LorReplyHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    LorUserRepository lorUserRepository,
    LorRecordRepository lorRecordRepository,
    CancellationTokenSource cancelToken)
{
    public const string LorTag = "#LorEdit";
    public const string LorKeySuffix = "Key";
    public const string LorContentEditSuffix = "ContentEdit";
    public const string LorContentSuffix = "Content";

    public async Task<TryAsync<Message>> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        return TryAsync(await ParseReplyText(replyText).MatchAsync(async tuple =>
            {
                var (key, lorChatId) = tuple;
                return replyText switch
                {
                    _ when !await IsLorUser(telegramId, tuple.LorChatId) => botClient.SendMessage(telegramId,
                        appConfig.LorConfig.NotLorUser, cancellationToken: cancelToken.Token),
                    _ when MatchTag(replyText, LorKeySuffix) => HandleLorKey(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorContentEditSuffix)
                        => HandleLorKeyEdit(messageText, lorChatId, telegramId),
                    _ when replyText.Contains(LorContentSuffix)
                        => HandleLorContent(key, messageText, lorChatId, telegramId),
                    _ => SendHelpMessage(telegramId)
                };
            },
            () => SendHelpMessage(telegramId)));
    }

    private bool MatchTag(string command, string tag)
    {
        var pattern = $"({LorTag}){tag}(:)";
        return Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase);
    }

    private static Option<(string Key, long LorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(LorTag) is [_, var keyAndId] &&
        keyAndId.Split(":") is [_, var maybeKey, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (maybeKey, lorChatId)
            : None;

    private async Task<bool> IsLorUser(long telegramId, long lorChatId) =>
        await lorUserRepository.IsLorUser((telegramId, lorChatId))
        || await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private async Task<Message> HandleLorContent(string key, string content, long lorChatId, long telegramId)
    {
        var lorConfig = appConfig.LorConfig;
        if (content.Length > lorConfig.LorContentLimit) return await SendHelpMessage(telegramId);

        // if (DateTime.UtcNow >)
        //     return await botClient.SendMessage(telegramId, lorConfig.LorKeyLimit, cancellationToken: cancelToken.Token);

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key.Trim()));
        if (!isExist) return await SendNotFound(key, telegramId);

        var isChange = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId, content);
        if (!isChange) return await SendFailedMessage(telegramId);

        var text = string.Format(lorConfig.LorContentSuccess, key, content);
        return await botClient.SendMessage(telegramId, text, cancellationToken: cancelToken.Token);
    }


    private async Task<Message> HandleLorKeyEdit(string key, long lorChatId, long telegramId)
    {
        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key));
        return isExist
            ? await SendSuccessKeyMessage(key, lorChatId, telegramId)
            : await SendNotFound(key, telegramId);
    }

    private async Task<Message> HandleLorKey(string key, long lorChatId, long telegramId)
    {
        var lorConfig = appConfig.LorConfig;
        if (key.Length > lorConfig.LorKeyLimit) return await SendHelpMessage(telegramId);

        var isExist = await lorRecordRepository.IsLorRecordExist((lorChatId, key));
        if (isExist)
        {
            var message = string.Format(lorConfig.LorKeyDuplicate, key);
            return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
        }

        var isAdd = await lorRecordRepository.AddLorRecord((lorChatId, key), telegramId, lorConfig.LorContentDefault);
        return isAdd
            ? await SendSuccessKeyMessage(key, lorChatId, telegramId)
            : await SendFailedMessage(telegramId);
    }

    private async Task<Message> SendSuccessKeyMessage(string key, long lorChatId, long telegramId)
    {
        var markup = new ForceReplyMarkup { InputFieldPlaceholder = $"Содержание для {key}..." };
        var message = string.Format(appConfig.LorConfig.LorKeyRequest, GetLorTag(LorContentSuffix, lorChatId, key));
        return await botClient.SendMessage(telegramId, message, replyMarkup: markup);
    }

    private async Task<Message> SendFailedMessage(long chatId)
    {
        var message = appConfig.LorConfig.LorFailed;
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendNotFound(string key, long telegramId)
    {
        var message = string.Format(appConfig.LorConfig.LorKeyNotFound, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendHelpMessage(long chatId)
    {
        var lorConfig = appConfig.LorConfig;
        var message = string.Format(lorConfig.LorHelp, lorConfig.LorKeyLimit, lorConfig.LorContentLimit);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    public static string GetLorTag(string suffix, long targetChatId, string key = "") =>
        $"{LorTag}{suffix}:{key}:{targetChatId}";
}
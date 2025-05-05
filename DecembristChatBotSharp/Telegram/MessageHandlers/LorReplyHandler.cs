using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class LorReplyHandler(
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    LorUserRepository lorUserRepository,
    LorRecordRepository lorRecordRepository,
    CancellationTokenSource cancelToken)
{
    public const string LorTag = "#Lor";
    public const string LorCreateSuffix = "Create";
    public const string LorEditSuffix = "Edit";

    public async Task<TryAsync<Message>> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return TryAsync(await ParseReplyText(replyText).MatchAsync(async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, LorTag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, LorTag);
                var (key, lorChatId) = tuple;
                return replyText switch
                {
                    _ when !await IsLorUser(telegramId, tuple.LorChatId) => botClient.SendMessage(telegramId,
                        appConfig.LorConfig.NotLorUser, cancellationToken: cancelToken.Token),
                    _ when MatchTag(replyText, LorCreateSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLorKey(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorCreateSuffix)
                        => HandleLorContent(key, messageText, lorChatId, telegramId, dateReply),
                    _ when MatchTag(replyText, LorEditSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLorKeyEdit(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorEditSuffix) =>
                        HandleLorContent(key, messageText, lorChatId, telegramId, dateReply),
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

    private async Task<Message> HandleLorContent(string key, string content, long lorChatId, long telegramId,
        DateTime date)
    {
        var lorConfig = appConfig.LorConfig;
        if (content.Length > lorConfig.LorContentLimit) return await SendHelpMessage(telegramId);

        if ((DateTime.UtcNow - date).TotalMinutes > 2)
            return await botClient.SendMessage(telegramId, $"Ключ:{key}\nВремя редактирования истекло, начните заново",
                cancellationToken: cancelToken.Token);

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
            ? await HandleLorRecord((lorChatId, key), telegramId)
            : await SendNotFound(key, telegramId);
    }

    private async Task<Message> HandleLorRecord(LorRecord.CompositeId id, long telegramId)
    {
        var record = await lorRecordRepository.GetLorRecord(id);
        return await record.MatchAsync(lorRecord => SendLorRecord(lorRecord, telegramId),
            () => SendNotFound(id.Record, telegramId));
    }

    private async Task<Message> SendLorRecord(LorRecord lorRecord, long telegramId)
    {
        var markup = new ForceReplyMarkup { InputFieldPlaceholder = "Впишите новое содержине..." };
        var message = string.Format("Key:{0},\nContent:\n{1},\n{2}", lorRecord.Id.Record, lorRecord.Content,
            GetLorTag(LorEditSuffix, lorRecord.Id.ChatId, lorRecord.Id.Record));
        return await botClient.SendMessage(telegramId, message, replyMarkup: markup,
            cancellationToken: cancelToken.Token);
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
        var message = string.Format(appConfig.LorConfig.LorKeyRequest, GetLorTag(LorCreateSuffix, lorChatId, key));
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
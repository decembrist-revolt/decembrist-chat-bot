using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class LorReplyHandler(
    LorRecordRepository lorRecordRepository,
    BotClient botClient,
    AppConfig appConfig,
    LorService lorService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    LorUserRepository lorUserRepository,
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
                    _ when !await IsLorUser(telegramId, tuple.LorChatId) => SendNotLorUserMessage(telegramId),
                    _ when MatchTag(replyText, LorCreateSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLorKey(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorCreateSuffix)
                           || MatchTag(replyText, LorEditSuffix) =>
                        HandleLorContent(key, messageText, lorChatId, telegramId, dateReply),
                    _ when MatchTag(replyText, LorEditSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLorEdit(messageText, lorChatId, telegramId),
                    _ => SendHelpMessage(telegramId)
                };
            },
            () => SendHelpMessage(telegramId)));
    }

    private Task<Message> SendNotLorUserMessage(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.LorConfig.NotLorUser, cancellationToken: cancelToken.Token);

    private static Option<(string Key, long LorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(LorTag) is [_, var keyAndId] &&
        keyAndId.Split(":") is [_, var maybeKey, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (maybeKey, lorChatId)
            : None;

    private async Task<bool> IsLorUser(long telegramId, long lorChatId) =>
        await lorUserRepository.IsLorUser((telegramId, lorChatId))
        || await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private bool MatchTag(string command, string tag)
    {
        var pattern = $"({LorTag}){tag}(:)";
        return Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase);
    }

    private async Task<Message> HandleLorKey(string key, long lorChatId, long telegramId)
    {
        var result = await lorService.HandleLorKey(key, lorChatId, telegramId);
        return result switch
        {
            LorResult.Success => await SendRequestContent(key, lorChatId, telegramId),
            LorResult.Duplicate => await SendDuplicateKey(key, telegramId),
            LorResult.Limit => await SendHelpMessage(telegramId),
            LorResult.Failed => await SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendDuplicateKey(string key, long telegramId)
    {
        var message = string.Format(appConfig.LorConfig.KeyDuplicate, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendRequestContent(string key, long lorChatId, long telegramId)
    {
        var message = string.Format(appConfig.LorConfig.KeyRequest,
            LorService.GetLorTag(LorCreateSuffix, lorChatId, key));
        return await botClient.SendMessage(telegramId, message, replyMarkup: lorService.GetContentTip());
    }

    private async Task<Message> HandleLorEdit(string key, long lorChatId, long telegramId)
    {
        var result = await lorService.HandleLorKeyEdit(key, lorChatId);
        return result switch
        {
            LorResult.Success => await SendLorRecord((lorChatId, key), telegramId),
            LorResult.NotFound => await SendNotFoundMessage(key, telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendLorRecord(LorRecord.CompositeId id, long telegramId)
    {
        var record = await lorRecordRepository.GetLorRecord(id);
        return await record.MatchAsync(lorRecord => SendRequestEdit(lorRecord, telegramId),
            () => SendNotFoundMessage(id.Record, telegramId));
    }

    private async Task<Message> SendRequestEdit(LorRecord lorRecord, long telegramId)
    {
        var message = string.Format("Key:{0},\nContent:\n{1},\n{2}", lorRecord.Id.Record, lorRecord.Content,
            LorService.GetLorTag(LorEditSuffix, lorRecord.Id.ChatId, lorRecord.Id.Record));
        return await botClient.SendMessage(telegramId, message, replyMarkup: lorService.GetContentTip(),
            cancellationToken: cancelToken.Token);
    }

    private async Task<Message> HandleLorContent(
        string key, string content, long lorChatId, long telegramId, DateTime date)
    {
        var result = await lorService.HandleLorContent(key, content, lorChatId, telegramId, date);
        return result switch
        {
            LorResult.Success => await SendSuccessContent(key, content, telegramId),
            LorResult.NotFound => await SendNotFoundMessage(key, telegramId),
            LorResult.Limit => await SendHelpMessage(telegramId),
            LorResult.Failed => await SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendSuccessContent(string key, string content, long telegramId)
    {
        var text = string.Format(appConfig.LorConfig.ContentSuccess, key, content);
        return await botClient.SendMessage(telegramId, text, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendNotFoundMessage(string key, long telegramId)
    {
        var message = string.Format(appConfig.LorConfig.KeyNotFound, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendFailedMessage(long chatId)
    {
        var message = appConfig.LorConfig.PrivateFailed;
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendHelpMessage(long chatId)
    {
        var lorConfig = appConfig.LorConfig;
        var message = string.Format(lorConfig.LorHelp, lorConfig.KeyLimit, lorConfig.ContentLimit);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }
}
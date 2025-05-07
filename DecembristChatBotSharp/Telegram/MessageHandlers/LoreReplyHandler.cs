using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class LoreReplyHandler(
    LoreRecordRepository loreRecordRepository,
    BotClient botClient,
    AppConfig appConfig,
    LoreService loreService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    LorUserRepository lorUserRepository,
    CancellationTokenSource cancelToken)
{
    public const string LoreTag = "#Lor";
    public const string LorCreateSuffix = "Create";
    public const string LorEditSuffix = "Edit";

    public async Task<TryAsync<Message>> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        Console.WriteLine(replyText);
        Console.WriteLine(messageText);
        return TryAsync(await ParseReplyText(replyText).MatchAsync(
            None: () => SendHelpMessage(telegramId),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, LoreTag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, LoreTag);
                var (key, lorChatId) = tuple;
                return replyText switch
                {
                    _ when !await IsLorUser(telegramId, tuple.LorChatId) => SendNotLoreUser(telegramId),
                    _ when MatchTag(replyText, LorCreateSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLoreKey(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorEditSuffix) && string.IsNullOrWhiteSpace(key)
                        => HandleLoreEdit(messageText, lorChatId, telegramId),
                    _ when MatchTag(replyText, LorCreateSuffix) || MatchTag(replyText, LorEditSuffix) =>
                        HandleLoreContent(key, messageText, lorChatId, telegramId, dateReply),
                    _ => SendHelpMessage(telegramId)
                };
            }));
    }

    private static Option<(string Key, long LorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(LoreTag) is [_, var keyAndId] &&
        keyAndId.Split(":") is [_, var maybeKey, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (maybeKey, lorChatId)
            : None;

    private async Task<bool> IsLorUser(long telegramId, long lorChatId) =>
        await lorUserRepository.IsLorUser((telegramId, lorChatId))
        || await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private bool MatchTag(string command, string tag)
    {
        var pattern = $"({LoreTag}){tag}(:)";
        return Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase);
    }

    private Task<Message> SendNotLoreUser(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.LoreConfig.NotLoreUser, cancellationToken: cancelToken.Token);

    #region KeyRegion

    private async Task<Message> HandleLoreKey(string key, long lorChatId, long telegramId)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.AddLoreKey(key, lorChatId, telegramId);
        result.LogLore(telegramId, lorChatId, key);
        return result switch
        {
            LoreResult.Success => await SendRequestContent(key, lorChatId, telegramId),
            LoreResult.Duplicate => await SendDuplicateKey(key, telegramId),
            LoreResult.Limit => await SendHelpMessage(telegramId),
            LoreResult.Failed => await SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendDuplicateKey(string key, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyDuplicate, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendRequestContent(string key, long lorChatId, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.ContentRequest,
            LoreService.GetLoreTag(LorCreateSuffix, lorChatId, key));
        return await botClient.SendMessage(telegramId, message, replyMarkup: loreService.GetContentTip());
    }

    #endregion

    private async Task<Message> HandleLoreEdit(string key, long lorChatId, long telegramId)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.ValidateKeyEdit(key, lorChatId);
        result.LogLore(telegramId, lorChatId, key);
        return result switch
        {
            LoreResult.Success => await RetrieveAndSendLoreRecord((lorChatId, key), telegramId),
            LoreResult.NotFound => await SendNotFoundMessage(key, telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> RetrieveAndSendLoreRecord(LoreRecord.CompositeId id, long telegramId)
    {
        var record = await loreRecordRepository.GetLoreRecord(id);
        return await record.MatchAsync(loreRecord => SendEditContentRequest(loreRecord, telegramId),
            () => SendFailedMessage(telegramId));
    }

    private async Task<Message> SendEditContentRequest(LoreRecord loreRecord, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.ChatTemplate, loreRecord.Id.Key, loreRecord.Content) +
                      LoreService.GetLoreTag(LorEditSuffix, loreRecord.Id.ChatId, loreRecord.Id.Key);
        return await botClient.SendMessage(telegramId, message, replyMarkup: loreService.GetContentTip(),
            cancellationToken: cancelToken.Token);
    }

    #region ContentRegion

    private async Task<Message> HandleLoreContent(
        string key, string content, long lorChatId, long telegramId, DateTime date)
    {
        key = key.ToLowerInvariant();
        var result = await loreService.ChangeLoreContent(key, content, lorChatId, telegramId, date);
        result.LogLore(telegramId, lorChatId, key, content);
        return result switch
        {
            LoreResult.Success => await SendSuccessContent(key, content, telegramId),
            LoreResult.NotFound => await SendNotFoundMessage(key, telegramId),
            LoreResult.Limit => await SendHelpMessage(telegramId),
            LoreResult.Failed => await SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<Message> SendSuccessContent(string key, string content, long telegramId)
    {
        var text = string.Format(appConfig.LoreConfig.ContentSuccess, key, content);
        return await botClient.SendMessage(telegramId, text, cancellationToken: cancelToken.Token);
    }

    #endregion

    private async Task<Message> SendNotFoundMessage(string key, long telegramId)
    {
        var message = string.Format(appConfig.LoreConfig.KeyNotFound, key);
        return await botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendFailedMessage(long chatId)
    {
        var message = appConfig.LoreConfig.PrivateFailed;
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }

    private async Task<Message> SendHelpMessage(long chatId)
    {
        var loreConfig = appConfig.LoreConfig;
        var message = string.Format(loreConfig.LoreHelp, loreConfig.KeyLimit, loreConfig.ContentLimit);
        return await botClient.SendMessage(chatId, message, cancellationToken: cancelToken.Token);
    }
}
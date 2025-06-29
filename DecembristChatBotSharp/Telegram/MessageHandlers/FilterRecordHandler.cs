using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class FilterRecordHandler(
    MessageAssistance messageAssistance,
    FilterService filterService,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken,
    BotClient botClient,
    AppConfig appConfig)
{
    public const string Tag = "#Filter";
    public const string RecordSuffix = "Record";
    public const string DeleteSuffix = "Delete";

    public async Task<Message> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return await ParseReplyText(replyText).MatchAsync(
            None: () => SendHelpMessage(telegramId),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, lorChatId) = tuple;
                return suffix switch
                {
                    _ when !await IsAdmin(telegramId, lorChatId) => SendNotAdmin(telegramId),
                    RecordSuffix => HandleCreateFilterRecord(messageText, lorChatId, telegramId, dateReply),
                    DeleteSuffix => HandleDeleteFilterRecord(messageText, lorChatId, telegramId, dateReply),
                    _ => SendHelpMessage(telegramId)
                };
            }).Flatten();
    }

    private async Task<Message> HandleDeleteFilterRecord(string messageText, long lorChatId, long telegramId,
        DateTime dateReply)
    {
        var record = messageText.ToLowerInvariant();
        var result = await filterService.DeleteFilterRecord(record, lorChatId, dateReply);
        filterService.LogFilter((byte)result, telegramId, lorChatId, record);
        return result switch
        {
            FilterDeleteResult.Success => await SendSuccessDelete(telegramId),
            FilterDeleteResult.NotFound => await SendNotFound(telegramId, record),
            FilterDeleteResult.Expire => await SendExpireMessage(telegramId, messageText),
            _ => await SendFailedMessage(telegramId)
        };
    }

    private async Task<Message> HandleCreateFilterRecord(
        string messageText, long targetChatId, long telegramId, DateTime dateReply)
    {
        var record = messageText.ToLower();
        var result = await filterService.HandleFilterRecord(record, targetChatId, dateReply);
        filterService.LogFilter((byte)result, telegramId, targetChatId, record);
        return result switch
        {
            FilterCreateResult.Success => await SendSuccessMessage(telegramId, record),
            FilterCreateResult.Expire => await SendExpireMessage(telegramId, record),
            FilterCreateResult.Duplicate => await SendDuplicateMessage(telegramId, record),
            FilterCreateResult.Failed => await SendFailedMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Message> SendSuccessDelete(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.FilterConfig.DeleteSuccess, cancellationToken: cancelToken.Token);

    private Task<Message> SendNotFound(long telegramId, string text)
    {
        var message = string.Format(appConfig.FilterConfig.NotFound, text);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendFailedMessage(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.FilterConfig.FailedMessage, cancellationToken: cancelToken.Token);

    private Task<Message> SendHelpMessage(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.FilterConfig.HelpMessage, cancellationToken: cancelToken.Token);

    private Task<Message> SendDuplicateMessage(long telegramId, string messageText)
    {
        var message = string.Format(appConfig.FilterConfig.DuplicateMessage, messageText);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendSuccessMessage(long telegramId, string text)
    {
        var message = string.Format(appConfig.FilterConfig.SuccessAddMessage, text);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendExpireMessage(long telegramId, string messageText)
    {
        var message = string.Format(appConfig.FilterConfig.ExpiredMessage, messageText);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private static Option<(string suffix, long lorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var recordAndId] &&
        recordAndId.Split(":") is [var suffix, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (suffix, lorChatId)
            : None;

    private async Task<bool> IsAdmin(long telegramId, long lorChatId) =>
        await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private Task<Message> SendNotAdmin(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.CommandConfig.AdminOnlyMessage,
            cancellationToken: cancelToken.Token);
}
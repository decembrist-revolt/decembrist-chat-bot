using DecembristChatBotSharp.Entity.Configs;
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
    ChatConfigService chatConfigService)
{
    public const string Tag = "#Filter";
    public const string RecordSuffix = "Record";
    public const string DeleteSuffix = "Delete";

    public async Task<Message> Do(Message message)
    {
        var telegramId = message.From!.Id;
        var chatId = message.Chat.Id;
        var maybeFilterConfig = await chatConfigService.GetConfig(chatId, config => config.FilterConfig);
        if (!maybeFilterConfig.TryGetSome(out var filterConfig))
            return await chatConfigService.LogNonExistConfig(SendHelpMessage(telegramId, filterConfig), nameof(FilterConfig));

        var maybeCommandConfig = await chatConfigService.GetConfig(chatId, config => config.CommandConfig);
        if (!maybeCommandConfig.TryGetSome(out var commandConfig))
            return await chatConfigService.LogNonExistConfig(SendHelpMessage(telegramId, filterConfig), nameof(CommandConfig));

        var replyText = message.ReplyToMessage!.Text;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return await ParseReplyText(replyText).MatchAsync(
            None: () => SendHelpMessage(telegramId, filterConfig),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, lorChatId) = tuple;
                return suffix switch
                {
                    _ when !await IsAdmin(telegramId, lorChatId) => SendNotAdmin(telegramId, commandConfig),
                    RecordSuffix => HandleCreateFilterRecord(messageText, lorChatId, telegramId, dateReply, filterConfig),
                    DeleteSuffix => HandleDeleteFilterRecord(messageText, lorChatId, telegramId, dateReply,filterConfig),
                    _ => SendHelpMessage(telegramId, filterConfig)
                };
            }).Flatten();
    }

    private async Task<Message> HandleDeleteFilterRecord(string messageText, long lorChatId, long telegramId,
        DateTime dateReply, FilterConfig filterConfig)
    {
        var record = messageText.ToLowerInvariant();
        var result = await filterService.DeleteFilterRecord(record, lorChatId, dateReply);
        filterService.LogFilter((byte)result, telegramId, lorChatId, record);
        return result switch
        {
            FilterDeleteResult.Success => await SendSuccessDelete(telegramId, filterConfig),
            FilterDeleteResult.NotFound => await SendNotFound(telegramId, record, filterConfig),
            FilterDeleteResult.Expire => await SendExpireMessage(telegramId, messageText, filterConfig),
            _ => await SendFailedMessage(telegramId, filterConfig)
        };
    }

    private async Task<Message> HandleCreateFilterRecord(
        string messageText, long targetChatId, long telegramId, DateTime dateReply, FilterConfig filterConfig)
    {
        var record = messageText.ToLower();
        var result = await filterService.HandleFilterRecord(record, targetChatId, dateReply);
        filterService.LogFilter((byte)result, telegramId, targetChatId, record);
        return result switch
        {
            FilterCreateResult.Success => await SendSuccessMessage(telegramId, record, filterConfig),
            FilterCreateResult.Expire => await SendExpireMessage(telegramId, record, filterConfig),
            FilterCreateResult.Duplicate => await SendDuplicateMessage(telegramId, record, filterConfig),
            FilterCreateResult.Failed => await SendFailedMessage(telegramId, filterConfig),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Message> SendSuccessDelete(long telegramId, FilterConfig filterConfig) =>
        botClient.SendMessage(telegramId, filterConfig.DeleteSuccess, cancellationToken: cancelToken.Token);

    private Task<Message> SendNotFound(long telegramId, string text, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.NotFound, text);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendFailedMessage(long telegramId, FilterConfig filterConfig) =>
        botClient.SendMessage(telegramId, filterConfig.FailedMessage, cancellationToken: cancelToken.Token);

    private Task<Message> SendHelpMessage(long telegramId, FilterConfig filterConfig) =>
        botClient.SendMessage(telegramId, filterConfig.HelpMessage, cancellationToken: cancelToken.Token);

    private Task<Message> SendDuplicateMessage(long telegramId, string messageText, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.DuplicateMessage, messageText);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendSuccessMessage(long telegramId, string text, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.SuccessAddMessage, text);
        return botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendExpireMessage(long telegramId, string messageText, FilterConfig filterConfig)
    {
        var message = string.Format(filterConfig.ExpiredMessage, messageText);
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

    private Task<Message> SendNotAdmin(long telegramId, CommandConfig commandConfig) =>
        botClient.SendMessage(telegramId, commandConfig.AdminOnlyMessage,
            cancellationToken: cancelToken.Token);
}
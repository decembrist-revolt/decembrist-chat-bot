using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

public class FilterRecordHandler(
    MessageAssistance messageAssistance,
    FilterService filterService,
    FilterRecordRepository filterRecordRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken,
    BotClient botClient,
    AppConfig appConfig)
{
    public const string Tag = "#Filter";
    public const string RecordSuffix = "Record";

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
                var (suffix, key, lorChatId) = tuple;
                var isEmpty = string.IsNullOrWhiteSpace(key);
                return suffix switch
                {
                    _ when !await IsAdmin(telegramId, lorChatId) => SendNotAdmin(telegramId),
                    RecordSuffix when isEmpty => HandleFilterRecord(messageText, lorChatId,
                        telegramId, dateReply),
                    _ => SendHelpMessage(telegramId)
                };
            }).Flatten();
    }

    private async Task<Message> HandleFilterRecord(string messageText, long targetChatId, long telegramId,
        DateTime date)
    {
        var result = await filterService.HandleFilterRecord(messageText, targetChatId, date);
        return result switch
        {
            FilterRecordResult.Success => await SendSuccessMessage(telegramId),
            FilterRecordResult.Expire => await SendExpireMessage(telegramId),
            FilterRecordResult.Duplicate => await SendDuplicateMessage(telegramId),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Message> SendHelpMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "/help", cancellationToken: cancelToken.Token);

    private Task<Message> SendDuplicateMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "/duplicate", cancellationToken: cancelToken.Token);

    private Task<Message> SendSuccessMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "/success", cancellationToken: cancelToken.Token);

    private Task<Message> SendExpireMessage(long telegramId) =>
        botClient.SendMessage(telegramId, "/expire", cancellationToken: cancelToken.Token);

    private static Option<(string suffix, string record, long lorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var recordAndId] &&
        recordAndId.Split(":") is [var suffix, var maybeRecord, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (suffix, maybeRecord, lorChatId)
            : None;

    private async Task<bool> IsAdmin(long telegramId, long lorChatId) =>
        await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private Task<Message> SendNotAdmin(long telegramId) =>
        botClient.SendMessage(telegramId, "/not admin", cancellationToken: cancelToken.Token);
}
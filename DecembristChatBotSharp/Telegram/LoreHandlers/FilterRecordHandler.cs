using DecembristChatBotSharp.Mongo;
using Telegram.Bot;
using Telegram.Bot.Types;
using static DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback.FilterCallbackHandler;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

public class FilterRecordHandler(
    MessageAssistance messageAssistance,
    LoreUserRepository loreUserRepository,
    AdminUserRepository adminUserRepository,
    CancellationTokenSource cancelToken,
    BotClient botClient,
    AppConfig appConfig)
{
    public async Task<Message> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return await ParseReplyText(replyText).MatchAsync(
            None: () => loreMessageAssistant.SendHelpMessage(telegramId),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, key, lorChatId) = tuple;
                var isEmpty = string.IsNullOrWhiteSpace(key);
                return suffix switch
                {
                    _ when !await IsLorUser(telegramId, lorChatId) => SendNotLoreUser(telegramId),
                    RecordSuffix when isEmpty => HandleFilterRecord(messageText, lorChatId, telegramId),
                    _ => SendHelpMessage(telegramId)
                };
            }).Flatten();
    }

    private Task<Message> HandleFilterRecord(string messageText, long lorChatId, long telegramId)
    {
        
        return botClient.SendMessage(telegramId, "/success", cancellationToken: cancelToken.Token);
    }

    private Task<Message> SendHelpMessage(long telegramId)
    {
        return botClient.SendMessage(telegramId, "/help", cancellationToken: cancelToken.Token);
    }

    private static Option<(string suffix, string record, long lorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var recordAndId] &&
        recordAndId.Split(":") is [var suffix, var maybeRecord, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (suffix, maybeRecord, lorChatId)
            : None;

    private async Task<bool> IsLorUser(long telegramId, long lorChatId) =>
        await loreUserRepository.IsLoreUser((telegramId, lorChatId))
        || await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private Task<Message> SendNotLoreUser(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.LoreConfig.NotLoreUser, cancellationToken: cancelToken.Token);
}
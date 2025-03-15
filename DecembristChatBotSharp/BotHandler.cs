using DecembristChatBotSharp.MessageHandlers;
using Serilog;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp;

public class BotHandler(long botTelegramId, AppConfig appConfig, BotClient botClient, Database db) : IUpdateHandler
{
    private readonly NewMemberHandler _newMemberHandler = new(appConfig, botClient, db);
    private readonly PrivateMessageHandler _privateMessageHandler = new(appConfig, botClient);
    private readonly ChatMessageHandler _chatMessageHandler = new(appConfig, botClient, db);
    private readonly ChatBotAddHandler _chatBotAddHandler = new(appConfig, botClient);

    public Task HandleUpdateAsync(BotClient client, Update update, CancellationToken cancelToken)
    {
        return update switch
        {
            {
                Type: UpdateType.Message,
                Message:
                {
                    Chat.Type: var chatType,
                } message
            } => chatType switch
            {
                ChatType.Private => HandlePrivateMessageUpdateAsync(message, cancelToken),
                _ => HandleMessageUpdateAsync(message, cancelToken),
            },
            {
                Type: UpdateType.ChatMember,
                ChatMember:
                {
                    Date: var date
                } chatMember
            } when IsValidUpdateDate(date) => HandleChatMemberUpdateAsync(chatMember, cancelToken),
            _ => Task.CompletedTask
        };
    }

    private Task HandlePrivateMessageUpdateAsync(Message message, CancellationToken cancelToken) => message switch
    {
        // private text message
        {
            From.Id: { },
        } => _privateMessageHandler.Do(message, cancelToken),
        _ => Task.CompletedTask
    };

    private Task HandleMessageUpdateAsync(Message message, CancellationToken cancelToken) => message switch
    {
        // bot added to chat
        {
            Type: MessageType.NewChatMembers,
            Chat.Id: var chatId,
            NewChatMembers: { Length: > 0 } newChatMembers
        } when newChatMembers.Exists(user => user.Id == botTelegramId) => _chatBotAddHandler.Do(chatId, cancelToken),
        // new message in chat
        {
            Date: var date,
            MessageId: var messageId,
            Chat.Id: var chatId,
            Text: var text,
            Sticker: var sticker,
            From.Id: var telegramId,
            Type: not MessageType.NewChatMembers and not MessageType.LeftChatMember
        } when IsValidUpdateDate(date) => _chatMessageHandler.Do(
            new ChatMessageHandlerParams(text ?? "", sticker?.FileId ?? "", messageId, telegramId, chatId), cancelToken),
        _ => Task.CompletedTask
    };

    private Task HandleChatMemberUpdateAsync(ChatMemberUpdated chatMember, CancellationToken cancelToken) =>
        chatMember switch
        {
            // new member joined chat
            {
                NewChatMember.Status: ChatMemberStatus.Member,
                Chat.Id: var chatId,
                From: { } user,
                ViaJoinRequest: false
            } => _newMemberHandler.Do(new NewMemberHandlerParams(chatId, user), cancelToken),
            _ => Task.CompletedTask
        };

    public Task HandleErrorAsync(
        BotClient botClient,
        Exception exception,
        HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        Log.Error(exception, "Error in {Source}", source);
        return Task.CompletedTask;
    }

    private bool IsValidUpdateDate(DateTime date) =>
        date > DateTime.UtcNow.AddSeconds(-appConfig.UpdateExpirationSeconds);
}

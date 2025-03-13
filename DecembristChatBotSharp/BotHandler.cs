using DecembristChatBotSharp.MessageHandlers;
using Serilog;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp;

public class BotHandler(AppConfig appConfig, BotClient botClient, Database db) : IUpdateHandler
{
    private readonly NewMemberHandler _newMemberHandler = new(appConfig, botClient, db);
    private readonly PrivateMessageHandler _privateMessageHandler = new(botClient);
    private readonly ChatMessageHandler _chatMessageHandler = new(appConfig, botClient, db);

    public Task HandleUpdateAsync(BotClient client, Update update, CancellationToken cancelToken)
    {
        return update switch
        {
            {
                Type: UpdateType.Message,
                Message:
                {
                    Date: var date,
                    Text: var text,
                    Type: MessageType.Text,
                    Chat:
                    {
                        Type: ChatType.Private,
                        Id: var chatId,
                    },
                    From.Id: var telegramId,
                }
            } when IsValidUpdateDate(date) => _privateMessageHandler.Do(
                new PrivateMessageHandlerParams(chatId, telegramId, text), cancelToken),
            {
                Type: UpdateType.ChatMember,
                ChatMember:
                {
                    Date: var date,
                    NewChatMember.Status: ChatMemberStatus.Member,
                    Chat.Id: { } chatId,
                    From: { } user,
                    ViaJoinRequest: false
                }
            } when IsValidUpdateDate(date) => _newMemberHandler.Do(new NewMemberHandlerParams(chatId, user),
                cancelToken),
            {
                Type: UpdateType.Message,
                Message:
                {
                    Date: var date,
                    MessageId: var messageId,
                    Chat.Id: var chatId,
                    Text: var text,
                    From.Id: var telegramId,
                    Type: not MessageType.NewChatMembers and not MessageType.LeftChatMember
                }
            } when IsValidUpdateDate(date) => _chatMessageHandler.Do(
                new ChatMessageHandlerParams(text ?? "", messageId, telegramId, chatId), cancelToken),
            _ => Task.CompletedTask
        };
    }

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
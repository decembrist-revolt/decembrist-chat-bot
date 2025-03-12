using DecembristChatBotSharp.MessageHandlers;
using Serilog;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp;

public class BotHandler(AppConfig appConfig, BotClient botClient, Database db) : IUpdateHandler
{
    private readonly CaptchaHandler _captchaHandler = new(appConfig, botClient, db);
    private readonly NewMemberHandler _newMemberHandler = new(appConfig, botClient, db);

    public Task HandleUpdateAsync(BotClient client, Update update, CancellationToken cancelToken)
    {
        return update switch
        {
            {
                Type: UpdateType.ChatMember,
                ChatMember:
                {
                    NewChatMember.Status: ChatMemberStatus.Member,
                    Chat.Id: { } chatId,
                    From: { } user,
                    ViaJoinRequest: false
                }
            } => _newMemberHandler.Do(new NewMemberHandlerParams(chatId, user, appConfig, botClient, db), cancelToken),
            {
                Type: UpdateType.Message,
                Message:
                {
                    MessageId: var messageId,
                    Chat.Id: var chatId,
                    Text: { } text,
                    From.Id: var telegramId
                }
            } => _captchaHandler.Do(new CaptchaHandlerParams(text, messageId, telegramId, chatId), cancelToken),
            _ => Task.CompletedTask
        };
    }

    public Task HandleErrorAsync(BotClient botClient, Exception exception, HandleErrorSource source,
        CancellationToken cancellationToken)
    {
        Log.Error(exception, "Error in {Source}", source);
        return Task.CompletedTask;
    }
}
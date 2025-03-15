﻿using DecembristChatBotSharp.Telegram.MessageHandlers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram;

public class BotHandler(
    [FromKeyedServices(DiContainer.BOT_TELEGRAM_ID)] Func<long> getBotTelegramId,
    BotClient botClient,
    AppConfig appConfig,
    CancellationToken cancelToken,
    NewMemberHandler newMemberHandler,
    PrivateMessageHandler privateMessageHandler,
    ChatMessageHandler chatMessageHandler,
    ChatBotAddHandler chatBotAddHandler
) : IUpdateHandler
{
    public void Start()
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates =
            [
                UpdateType.Message,
                UpdateType.ChatMember
            ],
            Offset = int.MaxValue
        };
        
        botClient.StartReceiving(this, receiverOptions, cancelToken);
    }
    
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
        } => privateMessageHandler.Do(message, cancelToken),
        _ => Task.CompletedTask
    };

    private Task HandleMessageUpdateAsync(Message message, CancellationToken cancelToken) => message switch
    {
        // bot added to chat
        {
            Type: MessageType.NewChatMembers,
            Chat.Id: var chatId,
            NewChatMembers: { Length: > 0 } newChatMembers
        } when IsBotInUsers(newChatMembers) => chatBotAddHandler.Do(chatId, cancelToken),
        // new message in chat
        {
            Date: var date,
            MessageId: var messageId,
            Chat.Id: var chatId,
            Text: var text,
            From.Id: var telegramId,
            Type: not MessageType.NewChatMembers and not MessageType.LeftChatMember
        } when IsValidUpdateDate(date) => chatMessageHandler.Do(
            new ChatMessageHandlerParams(text ?? "", messageId, telegramId, chatId), cancelToken),
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
            } => newMemberHandler.Do(new NewMemberHandlerParams(chatId, user), cancelToken),
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
    
    private bool IsBotInUsers(User[]? newChatMembers) =>
        newChatMembers?.Any(user => user.Id == getBotTelegramId()) == true;
}
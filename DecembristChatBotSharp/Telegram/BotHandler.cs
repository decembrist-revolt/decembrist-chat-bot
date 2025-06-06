﻿using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram;

[Singleton]
public class BotHandler(
    User botUser,
    BotClient botClient,
    AppConfig appConfig,
    NewMemberHandler newMemberHandler,
    PrivateMessageHandler privateMessageHandler,
    ChatMessageHandler chatMessageHandler,
    PrivateCallbackHandler privateCallbackHandler,
    ChatCallbackHandler chatCallbackHandler,
    ChatEditedHandler chatEditedHandler,
    TipsRegistrationService tipsRegistrationService,
    ChatBotAddHandler chatBotAddHandler,
    CancellationTokenSource cancelToken
) : IUpdateHandler
{
    public void Start()
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates =
            [
                UpdateType.Message,
                UpdateType.EditedMessage,
                UpdateType.ChatMember,
                UpdateType.CallbackQuery,
            ],
            Offset = int.MaxValue
        };

        botClient.StartReceiving(this, receiverOptions, cancelToken.Token);
    }

    public Task HandleUpdateAsync(BotClient client, Update update, CancellationToken cancellationToken)
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
                ChatType.Private => HandlePrivateMessageUpdateAsync(message),
                _ => HandleMessageUpdateAsync(message),
            },
            {
                Type: UpdateType.ChatMember,
                ChatMember:
                {
                    Date: var date
                } chatMember
            } when IsValidUpdateDate(date) => HandleChatMemberUpdateAsync(chatMember),
            {
                Type: UpdateType.EditedMessage,
                EditedMessage: { } editedMessage,
            } => HandleChatEditedMessage(editedMessage),
            {
                Type: UpdateType.CallbackQuery,
                CallbackQuery:
                {
                    Data: not null,
                    Message: not null
                } callbackQuery
            } => HandleCallback(callbackQuery),
            _ => Task.CompletedTask
        };
    }

    private Task HandleChatEditedMessage(Message message) => message switch
    {
        {
            From.Id: { },
        } => chatEditedHandler.Do(GetChatMessageHandlerParams(message)),
        _ => Task.CompletedTask
    };

    private Task HandlePrivateMessageUpdateAsync(Message message) => message switch
    {
        // private text message
        {
            From.Id: { },
        } => privateMessageHandler.Do(message),
        _ => Task.CompletedTask
    };

    private Task HandleMessageUpdateAsync(Message message) => message switch
    {
        // bot added to chat
        {
            Type: MessageType.NewChatMembers,
            Chat.Id: var chatId,
            NewChatMembers: { Length: > 0 } newChatMembers
        } when IsBotInUsers(newChatMembers) => chatBotAddHandler.Do(chatId, cancelToken.Token),
        // new message in chat
        {
            Date: var date,
            Type: not MessageType.NewChatMembers and not MessageType.LeftChatMember,
        } when IsValidUpdateDate(date) => HandleChatMessage(message),
        _ => Task.CompletedTask
    };

    private async Task HandleChatMessage(Message message)
    {
        var parameters = GetChatMessageHandlerParams(message);
        await chatMessageHandler.Do(parameters);
    }

    private ChatMessageHandlerParams GetChatMessageHandlerParams(Message message)
    {
        IMessagePayload payload = message switch
        {
            {
                Text: { } text,
                Type: MessageType.Text,
                From.Id: { }
            } => new TextPayload(text, CheckForLinkEntity(message.Entities)),
            {
                Caption: { } caption,
                From.Id: { }
            } => new TextPayload(caption, CheckForLinkEntity(message.Entities)),
            {
                Sticker.FileId: { } fileId,
                Type: MessageType.Sticker,
            } => new StickerPayload(fileId),
            _ => new UnknownPayload()
        };
        var messageId = message.MessageId;
        var telegramId = message.From!.Id;
        var chatId = message.Chat.Id;
        var parameters = new ChatMessageHandlerParams(
            payload, messageId, telegramId, chatId, Optional(message.ReplyToMessage?.From?.Id));
        return parameters;
    }

    private Task HandleCallback(CallbackQuery query)
    {
        var maybeCallbackParameters = CallbackService.ParseChatCallback(query.Data!);
        return maybeCallbackParameters.Match(
            None: () => Task.CompletedTask,
            Some: parameters =>
            {
                var callbackParameters = GetQueryParameters(query, parameters);
                return query switch
                {
                    {
                        Message.Chat.Type: ChatType.Private,
                    } => privateCallbackHandler.Do(callbackParameters),
                    not null => chatCallbackHandler.Do(callbackParameters),
                    _ => Task.CompletedTask
                };
            }
        );
    }

    private CallbackQueryParameters GetQueryParameters(CallbackQuery query, (string, string, string[]) parameters)
    {
        var (prefix, suffix, keysAndValue) = parameters;
        var queryParameters = CallbackService.GetQueryParameters(keysAndValue);
        var telegramId = query.From.Id;
        var messageId = query.Message!.MessageId;
        var chatId = query.Message.Chat.Id;
        var queryId = query.Id;

        return new CallbackQueryParameters(prefix, suffix, chatId, telegramId, messageId, queryId, queryParameters);
    }

    private bool CheckForLinkEntity(MessageEntity[]? entities) =>
        entities != null && entities.Any(e => e.Type is MessageEntityType.Url or MessageEntityType.TextLink);

    public async Task RegisterTipsCommand() => await tipsRegistrationService.RegisterTipsCommand();

    private Task HandleChatMemberUpdateAsync(ChatMemberUpdated chatMember) =>
        chatMember switch
        {
            // new member joined chat
            {
                NewChatMember.Status: ChatMemberStatus.Member,
                Chat.Id: var chatId,
                From: { } user,
                ViaJoinRequest: false
            } => newMemberHandler.Do(new NewMemberHandlerParams(chatId, user)),
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
        newChatMembers?.Any(user => user.Id == botUser.Id) == true;
}
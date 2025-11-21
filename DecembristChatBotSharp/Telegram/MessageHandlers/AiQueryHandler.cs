using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using FastExpressionCompiler;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class AiQueryHandler(
    User botUser,
    DeepSeekService deepSeekService,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    BotClient botClient,
    AppConfig appConfig,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        // Check if bot is mentioned or message is a reply to bot
        if (parameters is { BotMentioned: false, ReplyToBotMessage: false }) return false;

        // Only process text messages
        if (parameters.Payload is not TextPayload { Text: var text }) return false;

        var (messageId, telegramId, chatId) = parameters;

        // Remove bot mention from text if present
        var queryText = RemoveBotMention(text);
        if (string.IsNullOrWhiteSpace(queryText)) return false;

        var isAdmin = await adminUserRepository.IsAdmin((telegramId, chatId));

        // Try to use AI token
        var result = await memberItemService.UseAiToken(chatId, telegramId, isAdmin);

        return result switch
        {
            AiTokenResult.NoItems => await SendNoItemsMessage(chatId, messageId),
            AiTokenResult.Success => await ProcessAiQuery(chatId, telegramId, messageId, queryText),
            AiTokenResult.Failed => await SendFailedMessage(chatId, messageId),
            _ => false
        };
    }

    private string RemoveBotMention(string text)
    {
        // Remove @botname mention from the text
        var username = botUser.Username;
        if (string.IsNullOrEmpty(username)) return text;

        var mention = $"@{username}";
        return text.Replace(mention, "").Trim();
    }

    private async Task<bool> ProcessAiQuery(long chatId, long telegramId, int messageId, string queryText)
    {
        var maybeSent =
            from sentMessageId in SendThinkingMessage(chatId, messageId).ToTryOptionAsync()
            select SendToDeepSeek(chatId, telegramId, queryText, sentMessageId);
        
        return await maybeSent.IfNoneOrFail(async () =>
        {
            await SendAiErrorMessage(chatId, messageId);
            return false;
        });
    }

    private async Task<Option<int>> SendThinkingMessage(long chatId, int replyToMessageId) =>
        await botClient.SendMessage(
                chatId,
                appConfig.DeepSeekConfig!.ThinkingMessage,
                replyParameters: new ReplyParameters { MessageId = replyToMessageId },
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(message =>
                {
                    Log.Information("Sent thinking message {MessageId} in chat {ChatId}", message.MessageId, chatId);
                    return Some(message.MessageId);
                }, ex =>
                {
                    Log.Error(ex, "Failed to send thinking message to chat {ChatId}", chatId);
                    return None;
                }
            );

    private async Task<bool> SendToDeepSeek(long chatId, long telegramId, string queryText, int sentMessageId) =>
        await deepSeekService.GetChatResponse(queryText, chatId, telegramId)
            .ToTryOptionAsync()
            .MatchAsync(
                async response =>
                {
                    await EditMessageWithAiResponse(chatId, sentMessageId, response);
                    return true;
                },
                async () =>
                {
                    await EditMessageWithError(chatId, sentMessageId);
                    return false;
                }, async ex =>
                {
                    Log.Error(ex, "Error processing AI query for user {UserId} in chat {ChatId}", telegramId, chatId);
                    await EditMessageWithError(chatId, sentMessageId);
                    return false;
                }
            );

    private async Task EditMessageWithAiResponse(long chatId, int messageId, string response)
    {
        await botClient.EditMessageAndLog(
            chatId,
            messageId,
            response,
            _ => Log.Information("Edited message {MessageId} with AI response in chat {ChatId}", messageId, chatId),
            ex => Log.Error(ex, "Failed to edit message {MessageId} with AI response in chat {ChatId}", messageId,
                chatId),
            cancelToken.Token);
    }

    private async Task EditMessageWithError(long chatId, int messageId)
    {
        await botClient.EditMessageAndLog(
            chatId,
            messageId,
            appConfig.DeepSeekConfig!.AiErrorMessage,
            _ => Log.Information("Edited message {MessageId} with error in chat {ChatId}", messageId, chatId),
            ex => Log.Error(ex, "Failed to edit message {MessageId} with error in chat {ChatId}", messageId, chatId),
            cancelToken.Token);
    }

    private async Task<bool> SendNoItemsMessage(long chatId, int messageId)
    {
        await botClient.SendMessageAndLog(
            chatId,
            appConfig.DeepSeekConfig!.NoTokensMessage,
            replyMessageId: messageId,
            _ => Log.Information("Sent no AI tokens message to chat {ChatId}", chatId),
            ex => Log.Error(ex, "Failed to send no AI tokens message to chat {ChatId}", chatId),
            cancelToken.Token);
        return true;
    }

    private async Task<bool> SendFailedMessage(long chatId, int messageId)
    {
        await botClient.SendMessageAndLog(
            chatId,
            appConfig.DeepSeekConfig!.FailedToUseTokenMessage,
            replyMessageId: messageId,
            _ => Log.Information("Sent AI token failed message to chat {ChatId}", chatId),
            ex => Log.Error(ex, "Failed to send AI token failed message to chat {ChatId}", chatId),
            cancelToken.Token);
        return true;
    }

    private async Task<bool> SendAiErrorMessage(long chatId, int messageId)
    {
        await botClient.SendMessageAndLog(
            chatId,
            appConfig.DeepSeekConfig!.AiErrorMessage,
            replyMessageId: messageId,
            _ => Log.Information("Sent AI error message to chat {ChatId}", chatId),
            ex => Log.Error(ex, "Failed to send AI error message to chat {ChatId}", chatId),
            cancelToken.Token);
        return true;
    }
}
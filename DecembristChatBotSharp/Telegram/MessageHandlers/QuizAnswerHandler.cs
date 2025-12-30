using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class QuizAnswerHandler(
    AppConfig appConfig,
    QuizService quizService,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    private const string EyesReaction = "\ud83d\udc40"; // 👀

    /// <summary>
    /// Check if there's an active quiz and record the answer if message is a reply to the question
    /// </summary>
    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        if (appConfig.QuizConfig is not { Enabled: true }) return false;

        // Only process text messages
        if (parameters.Payload is not TextPayload { Text: var text }) return false;

        // Ignore bot commands
        if (text.StartsWith('/')) return false;

        // Ignore very short answers (less than 2 characters)
        if (text.Length < 2) return false;

        // Answer must be a reply to some message
        if (parameters.ReplyToMessageId.IsNone) return false;

        // Record answer for later validation (service will check if it's reply to quiz question)
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;

        return await parameters.ReplyToMessageId
            .MapAsync(replyMessageId => quizService.RecordAnswer(chatId, telegramId, messageId, text, replyMessageId))
            .MatchAsync(async recorded =>
            {
                if (recorded == AnswerResult.AnswerRecorded)
                {
                    Log.Debug("Recorded quiz answer from user {UserId} in chat {ChatId}: {Answer}",
                        telegramId, chatId, text);

                    // Set eyes reaction to indicate answer is recorded
                    await botClient.SetMessageReaction(
                            chatId,
                            messageId,
                            [new ReactionTypeEmoji { Emoji = EyesReaction }],
                            cancellationToken: cancelToken.Token
                        ).ToTryAsync()
                        .IfFail(ex =>
                            Log.Error(ex, "Failed to set reaction on quiz answer message {MessageId}", messageId));
                    return true;
                }

                if (recorded == AnswerResult.AnswerNotRecorded) return true;

                return recorded != AnswerResult.QuestionNonExist;
            }, () => Task.FromResult(false));
    }
}
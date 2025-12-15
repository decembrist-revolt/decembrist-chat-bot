using System.Text.Json;
using System.Text.Json.Serialization;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Service;

public record QuizGenerationRequest(
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("parent_message_id")]
    string ParentMessageId
);

public record QuizGenerationResponse(
    [property: JsonPropertyName("question")]
    string Question,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("subtopic")]
    string Subtopic
);

public record QuizValidationRequest(
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("parent_message_id")]
    string ParentMessageId
);

public record QuizValidationResponse(
    [property: JsonPropertyName("is_correct")]
    bool IsCorrect
);

public record BatchValidationResult(
    [property: JsonPropertyName("user_id")]
    long UserId,
    [property: JsonPropertyName("is_correct")]
    bool IsCorrect
);

public record BatchValidationResponse(
    [property: JsonPropertyName("results")]
    List<BatchValidationResult> Results
);

[Singleton]
public class QuizService(
    AppConfig appConfig,
    QuizRepository quizRepository,
    QuizSubtopicHistoryRepository subtopicHistoryRepository,
    DeepSeekService deepSeekService,
    MemberItemRepository memberItemRepository,
    HistoryLogRepository historyLogRepository,
    MongoDatabase mongoDatabase,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    /// <summary>
    /// Extract JSON from markdown code block if present
    /// </summary>
    private static string ExtractJsonFromMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Remove ```json and ``` markers
        var cleaned = text.Trim();

        if (cleaned.StartsWith("```json"))
        {
            cleaned = cleaned["```json".Length..];
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned["```".Length..];
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3];
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Generate a new quiz question using DeepSeek
    /// </summary>
    public async Task<Option<QuizGenerationResponse>> GenerateQuizQuestion()
    {
        var topics = appConfig.QuizConfig.Topics;
        if (topics.Count == 0)
        {
            Log.Warning("No quiz topics configured");
            return None;
        }

        var topic = string.Join(", ", topics);

        // Get recent subtopics to avoid repetition
        var historyOption = await subtopicHistoryRepository.GetHistory();
        var recentSubtopics = historyOption.Match(
            h => h.RecentSubtopics,
            () => []
        );

        var subtopicsInfo = recentSubtopics.Count > 0
            ? string.Format(appConfig.QuizConfig.SubtopicAvoidancePrompt, string.Join(", ", recentSubtopics))
            : "";

        var prompt = string.Format(appConfig.QuizConfig.QuestionGenerationPrompt, topic) + subtopicsInfo;

        var response = await deepSeekService.GetChatResponse(prompt, 0, 0);

        return await response
            .Map(ExtractJsonFromMarkdown)
            .Map(jsonText => JsonSerializer.Deserialize<QuizGenerationResponse>(jsonText))
            .ToTryOption()
            .MatchAsync(async quizResponse =>
                {
                    if (quizResponse == null || string.IsNullOrWhiteSpace(quizResponse.Question) ||
                        string.IsNullOrWhiteSpace(quizResponse.Answer))
                    {
                        Log.Warning("DeepSeek returned invalid quiz format");
                        return None;
                    }

                    // Save subtopic to history
                    if (!string.IsNullOrWhiteSpace(quizResponse.Subtopic))
                    {
                        await subtopicHistoryRepository.AddSubtopic(
                            quizResponse.Subtopic,
                            appConfig.QuizConfig.SubtopicHistoryLimit);

                        Log.Information("Quiz generated with subtopic: {Subtopic}", quizResponse.Subtopic);
                    }

                    return Optional(quizResponse);
                },
                () => Task.FromResult(Option<QuizGenerationResponse>.None),
                ex =>
                {
                    Log.Error(ex, "Failed to parse DeepSeek quiz response: {Response}", response.ToString());
                    return Task.FromResult(Option<QuizGenerationResponse>.None);
                }
            );
    }

    /// <summary>
    /// Send quiz question to a chat
    /// </summary>
    public async Task<Option<QuizQuestion>> SendQuizToChat(long chatId, string question, string answer)
    {
        var messageText = string.Format(appConfig.QuizConfig!.QuestionMessageTemplate, question.EscapeMarkdown());

        return await botClient.SendMessage(chatId, messageText, parseMode: ParseMode.Markdown,
                cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(async message =>
            {
                var questionId = Guid.NewGuid().ToString();
                var id = new QuizQuestion.CompositeId(chatId, questionId);
                var quizQuestion = new QuizQuestion(id, question, answer, DateTime.UtcNow, message.MessageId, []);

                var saved = await quizRepository.SaveQuestion(quizQuestion);
                return saved ? Some(quizQuestion) : None;
            }, ex =>
            {
                Log.Error(ex, "Failed to send quiz question to chat {ChatId}", chatId);
                return Task.FromResult(Option<QuizQuestion>.None);
            });
    }

    /// <summary>
    /// Record user answer for later validation (only if it's a reply to the question message)
    /// Rate limit: 1 answer per minute per user
    /// </summary>
    public async Task<bool> RecordAnswer(
        long chatId, long telegramId, int messageId, string answerText, Option<int> replyToMessageId)
    {
        // Answer must be a reply to the question message
        if (replyToMessageId.IsNone) return false;

        var activeQuestion = await quizRepository.GetActiveQuestion(chatId);

        return await activeQuestion.MatchAsync(async question =>
            {
                var replyId = replyToMessageId.IfNone(0);

                // Check if reply is to the quiz question message
                if (replyId != question.MessageId) return false;

                // Rate limiting: check if user already answered in the last minute
                var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
                var recentAnswerFromUser = question.Answers
                    .Any(a => a.TelegramId == telegramId && a.AnsweredAtUtc > oneMinuteAgo);

                if (recentAnswerFromUser)
                {
                    Log.Debug("User {UserId} tried to answer too quickly (rate limit: 1/min)", telegramId);
                    return false;
                }

                var answerData = new QuizAnswerData(messageId, telegramId, answerText, DateTime.UtcNow);
                return await quizRepository.AddAnswer(chatId, answerData);
            },
            () => Task.FromResult(false)
        );
    }

    /// <summary>
    /// Validate answer using DeepSeek
    /// </summary>
    public async Task<Option<bool>> ValidateAnswer(string question, string correctAnswer, string userAnswer)
    {
        var prompt = string.Format(
            appConfig.QuizConfig!.AnswerValidationPrompt,
            question,
            correctAnswer,
            userAnswer
        );

        var response = await deepSeekService.GetChatResponse(prompt, 0, 0);

        return await response.MatchAsync(async responseText =>
            {
                var jsonText = ExtractJsonFromMarkdown(responseText);
                return await Try(() => JsonSerializer.Deserialize<QuizValidationResponse>(jsonText))
                    .ToAsync()
                    .Match(validationResponse =>
                        {
                            if (validationResponse == null)
                            {
                                Log.Warning("DeepSeek returned invalid validation format");
                                return None;
                            }

                            Log.Information("Quiz validation: {IsCorrect}", validationResponse.IsCorrect);

                            return Some(validationResponse.IsCorrect);
                        },
                        ex =>
                        {
                            Log.Error(ex, "Failed to parse DeepSeek validation response: {Response}", responseText);
                            return None;
                        }
                    );
            },
            () => Task.FromResult(Option<bool>.None)
        );
    }

    /// <summary>
    /// Validate multiple answers in batch using DeepSeek
    /// </summary>
    public async Task<Dictionary<long, bool>> ValidateAnswersBatch(
        string question, string correctAnswer, List<QuizAnswerData> answers)
    {
        if (answers.Count == 0) return [];

        var maxAllowedLength = correctAnswer.Length * 3;
        var resultsMap = new Dictionary<long, bool>();

        // Filter answers by length - too long answers are automatically wrong
        var validLengthAnswers = new List<QuizAnswerData>();

        foreach (var answer in answers)
        {
            if (answer.Answer.Length > maxAllowedLength)
            {
                // Too long - automatically wrong
                resultsMap[answer.TelegramId] = false;
                Log.Information("User {UserId}: False (answer too long: {Length} > {MaxLength})",
                    answer.TelegramId, answer.Answer.Length, maxAllowedLength);
            }
            else
            {
                validLengthAnswers.Add(answer);
            }
        }

        // If no valid length answers, return results
        if (validLengthAnswers.Count == 0) return resultsMap;

        // Build batch prompt with valid answers
        var answersText = string.Join("\n", validLengthAnswers.Select(a =>
            $"user_id: {a.TelegramId}, answer: \"{a.Answer}\""));

        var prompt = string.Format(
            appConfig.QuizConfig!.BatchAnswerValidationPrompt, question, correctAnswer, answersText);

        var response = await deepSeekService.GetChatResponse(prompt, 0, 0);

        return response.ToTryOption()
            .Map(ExtractJsonFromMarkdown)
            .Map(jsonText => JsonSerializer.Deserialize<BatchValidationResponse>(jsonText))
            .Match(batchResponse =>
                {
                    if (batchResponse?.Results == null || batchResponse.Results.Count == 0)
                    {
                        Log.Warning("DeepSeek returned invalid batch validation format");
                        return resultsMap;
                    }

                    Log.Information("Batch validation completed: {Count} answers validated",
                        batchResponse.Results.Count);

                    // Add DeepSeek results to map
                    foreach (var result in batchResponse.Results)
                    {
                        resultsMap[result.UserId] = result.IsCorrect;
                        Log.Information("User {UserId}: {IsCorrect}", result.UserId, result.IsCorrect);
                    }

                    return resultsMap;
                },
                () => resultsMap,
                ex =>
                {
                    Log.Error(ex, "Failed to parse DeepSeek batch validation response: {Response}",
                        response.ToString());
                    return resultsMap;
                });
    }

    /// <summary>
    /// Process pending answers and reward winners
    /// </summary>
    public async Task ProcessPendingAnswers()
    {
        var questionsWithAnswers = await quizRepository.GetQuestionsWithPendingAnswers();
        if (questionsWithAnswers.IsEmpty)
        {
            return;
        }

        foreach (var question in questionsWithAnswers)
        {
            await ProcessAnswersForQuestion(question);
        }
    }

    /// <summary>
    /// Close unanswered quiz question and edit message to show correct answer
    /// </summary>
    public async Task CloseUnansweredQuestion(QuizQuestion question)
    {
        // Edit original question message to show it's closed with correct answer
        var editedText = string.Format(
            appConfig.QuizConfig!.QuizUnansweredTemplate,
            question.Question.EscapeMarkdown(),
            question.CorrectAnswer.EscapeMarkdown()
        );

        await botClient.EditMessageText(
                question.Id.ChatId,
                question.MessageId,
                editedText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancelToken.Token
            ).ToTryAsync()
            .IfFail(ex => Log.Warning(ex, "Failed to edit closed quiz message {MessageId} in chat {ChatId}",
                question.MessageId, question.Id.ChatId));

        Log.Information("Quiz question closed without answer in chat {ChatId}, age: {Age}",
            question.Id.ChatId, DateTime.UtcNow - question.CreatedAtUtc);
    }

    private async Task ProcessAnswersForQuestion(QuizQuestion question)
    {
        if (question.Answers.Count == 0)
        {
            return;
        }

        // Sort answers by time (first answer wins if correct)
        var sortedAnswers = question.Answers.OrderBy(a => a.AnsweredAtUtc).ToList();

        // Validate all answers in batch
        var validationResults = await ValidateAnswersBatch(question.Question, question.CorrectAnswer, sortedAnswers);

        if (validationResults.Count == 0)
        {
            Log.Warning("Batch validation failed for question {QuestionId}, will retry later", question.Id.QuestionId);
            return;
        }

        // Find first correct answer by checking each answer in time order
        QuizAnswerData? firstCorrectAnswer = null;

        foreach (var answer in sortedAnswers)
        {
            var isCorrect = validationResults.TryGetValue(answer.TelegramId, out var correct) && correct;

            if (isCorrect && firstCorrectAnswer == null)
            {
                firstCorrectAnswer = answer;
                break; // Stop at first correct
            }
        }

        if (firstCorrectAnswer != null)
        {
            // Winner found!
            await RewardWinner(question.Id.ChatId, firstCorrectAnswer.TelegramId, question, firstCorrectAnswer);
            // Delete entire question (with all answers)
            await quizRepository.DeleteQuestion(question.Id);
        }
        else
        {
            // No correct answers, remove all wrong answers by messageId
            foreach (var answer in sortedAnswers)
            {
                var isCorrect = validationResults.TryGetValue(answer.TelegramId, out var correct) && correct;
                if (!isCorrect)
                {
                    await quizRepository.RemoveAnswer(question.Id.ChatId, answer.MessageId);
                }
            }
        }
    }

    private async Task RewardWinner(long chatId, long telegramId, QuizQuestion question, QuizAnswerData answer)
    {
        var session = await mongoDatabase.OpenSession();
        session.StartTransaction();

        // Give Box to winner
        var itemAdded = await memberItemRepository.AddMemberItem(chatId, telegramId, MemberItemType.Box, session);

        if (itemAdded)
        {
            // Log to history
            await historyLogRepository.LogItem(
                chatId, telegramId, MemberItemType.Box, 1, MemberItemSourceType.Admin, session);

            // Get winner username
            var userName = await botClient.GetUsernameOrId(telegramId, chatId, cancelToken.Token);

            // Edit original question message with answer and winner
            var editedText = string.Format(
                appConfig.QuizConfig!.QuizCompletedTemplate,
                question.Question.EscapeMarkdown(),
                question.CorrectAnswer.EscapeMarkdown(),
                userName.EscapeMarkdown()
            );

            var isEdit = await botClient.EditMessageText(
                chatId,
                question.MessageId,
                editedText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancelToken.Token
            ).ToTryAsync().Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to edit quiz question message {MessageId} in chat {ChatId}",
                        question.MessageId, chatId);
                    return false;
                });
            if (!isEdit)
            {
                await session.TryAbort(cancelToken.Token);
                return;
            }

            // Send congratulations message as reply to winner's answer
            var congratsMessage = string.Format(appConfig.QuizConfig!.WinnerMessageTemplate, userName.EscapeMarkdown());

            var isSend = await botClient.SendMessage(
                chatId,
                congratsMessage,
                parseMode: ParseMode.Markdown,
                replyParameters: new ReplyParameters { MessageId = answer.MessageId },
                cancellationToken: cancelToken.Token
            ).ToTryAsync().Match(_ => true,
                ex =>
                {
                    Log.Error(ex, "Failed to send quiz winner message to chat {ChatId}", chatId);
                    return false;
                });
            if (!isSend)
            {
                await session.TryAbort(cancelToken.Token);
                return;
            }

            Log.Information("Quiz winner: user {UserId} in chat {ChatId} for question {QuestionId}",
                telegramId, chatId, question.Id.QuestionId);

            await session.TryCommit(cancelToken.Token);
            return;
        }

        Log.Error("Failed to add Box to quiz winner {UserId} in chat {ChatId}", telegramId, chatId);
        await session.TryAbort(cancelToken.Token);
    }
}
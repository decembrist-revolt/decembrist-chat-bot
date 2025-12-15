namespace DecembristChatBotSharp.Entity;

/// <summary>
/// Active quiz question in a chat with embedded answers
/// </summary>
public record QuizQuestion(
    QuizQuestion.CompositeId Id,
    string Question,
    string CorrectAnswer,
    DateTime CreatedAtUtc,
    int MessageId,
    List<QuizAnswerData> Answers
)
{
    public record CompositeId(long ChatId, string QuestionId);
}

/// <summary>
/// Answer embedded in the quiz question document
/// </summary>
public record QuizAnswerData(
    int MessageId,
    long TelegramId,
    string Answer,
    DateTime AnsweredAtUtc
);

public record QuizHistoryLogData(
    string Question,
    string Topic
) : IHistoryLogData;


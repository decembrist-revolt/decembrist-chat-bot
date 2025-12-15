using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

/// <summary>
/// Stores the history of quiz subtopics to ensure variety
/// </summary>
public record QuizSubtopicHistory(
    [property: BsonId] string Id, // Use single ID since it's global
    List<string> RecentSubtopics,
    DateTime LastUpdatedUtc
)
{
    public const string GlobalId = "quiz_subtopic_history";
}


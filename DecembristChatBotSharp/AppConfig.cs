using System.Text.Json;

namespace DecembristChatBotSharp;

public record AppConfig(
    string WelcomeMessage,
    string DatabaseFile,
    long CheckCaptchaIntervalSeconds,
    long CaptchaTimeSeconds,
    string CaptchaAnswer,
    string JoinText
)
{
    public static Option<AppConfig> GetInstance()
    {
        var jsonString = File.ReadAllText("application.json");
        return Optional(JsonSerializer.Deserialize<AppConfig>(jsonString));
    }
}
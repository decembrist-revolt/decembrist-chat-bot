using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DecembristChatBotSharp;

public record AppConfig(
    string TelegramBotToken,
    string WelcomeMessage,
    string DatabaseFile,
    long CheckCaptchaIntervalSeconds,
    long CaptchaTimeSeconds,
    string CaptchaAnswer,
    string JoinText,
    string CaptchaFailedText,
    int CaptchaRetryCount,
    int UpdateExpirationSeconds,
    AllowedChatConfig AllowedChatConfig,
    Dictionary<string, string> FastReply)
{
    public static Option<AppConfig> GetInstance()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
        return config.Get<AppConfig>();
    }
}

public record AllowedChatConfig(
    System.Collections.Generic.HashSet<long> AllowedChatIds,
    string WrongChatText,
    string RightChatText
);
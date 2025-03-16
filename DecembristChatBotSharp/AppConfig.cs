using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DecembristChatBotSharp;

public record AppConfig(
    [Required(AllowEmptyStrings = false)]
    string TelegramBotToken,
    [Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [Required(AllowEmptyStrings = false)]
    string DatabaseFile,
    long CheckCaptchaIntervalSeconds,
    long CaptchaTimeSeconds,
    [Required(AllowEmptyStrings = false)]
    string CaptchaAnswer,
    [Required(AllowEmptyStrings = false)]
    string JoinText,
    [Required(AllowEmptyStrings = false)]
    string CaptchaFailedText,
    int CaptchaRetryCount,
    int UpdateExpirationSeconds,
    AllowedChatConfig AllowedChatConfig,
    Dictionary<string, string> FastReply,
    PersistentConfig PersistentConfig,
    DateTime? DeployTime = null,
    List<long>? WhiteListIds = null)
{
    public static Option<AppConfig> GetInstance()
    {
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [nameof(DeployTime)] = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture)
            }!);
        
        return Try(() => configBuilder.Build())
            .Map(config => config.Get<AppConfig>())
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to read appsettings.json");
                throw ex;
            });
    }
}

public record AllowedChatConfig(
    System.Collections.Generic.HashSet<long>? AllowedChatIds,
    [Required(AllowEmptyStrings = false)]
    string WrongChatText,
    [Required(AllowEmptyStrings = false)]
    string RightChatText
);

public record PersistentConfig(
    bool Persistent,
    string Source,
    S3Config? S3Config,
    int PersistenceLagSeconds
);

public record S3Config(
    [Required(AllowEmptyStrings = false)]
    string BucketName,
    [Required(AllowEmptyStrings = false)]
    string ServiceUrl,
    [Required(AllowEmptyStrings = false)]
    string Region,
    [Required(AllowEmptyStrings = false)]
    string AccessKey,
    [Required(AllowEmptyStrings = false)]
    string SecretKey
);
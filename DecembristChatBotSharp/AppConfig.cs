using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DecembristChatBotSharp;

public record AppConfig(
    [property: Required(AllowEmptyStrings = false)]
    string TelegramBotToken,
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DatabaseFile,
    long CheckCaptchaIntervalSeconds,
    long CaptchaTimeSeconds,
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaAnswer,
    [property: Required(AllowEmptyStrings = false)]
    string JoinText,
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaFailedText,
    int CaptchaRetryCount,
    int UpdateExpirationSeconds,
    AllowedChatConfig AllowedChatConfig,
    MongoConfig MongoConfig,
    CommandConfig CommandConfig,
    RedditConfig RedditConfig,
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
            .Map(config => config.Get<AppConfig>() ?? throw new Exception("AppConfig is null"))
            .Do(config => Validator.ValidateObject(config, new ValidationContext(config), validateAllProperties: true))
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to read appsettings.json");
                throw ex;
            });
    }
}

public record AllowedChatConfig(
    System.Collections.Generic.HashSet<long>? AllowedChatIds,
    [property: Required(AllowEmptyStrings = false)]
    string WrongChatText,
    [property: Required(AllowEmptyStrings = false)]
    string RightChatText
);

public record MongoConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ConnectionString,
    [property: Required(AllowEmptyStrings = false)]
    string DatabaseName
);

public record CommandConfig(
    int TopLikeMemberCount,
    int CommandIntervalSeconds,
    [property: Required(AllowEmptyStrings = false)]
    string LikeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LikeReceiverNotSet,
    [property: Required(AllowEmptyStrings = false)]
    string CommandNotReady,
    [property: Required(AllowEmptyStrings = false)]
    string SelfLikeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AdminOnlyMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyHelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StickerNotFoundMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyDuplicateMessage
);

public record RedditConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ClientId,
    [property: Required(AllowEmptyStrings = false)]
    string ClientSecret,
    [property: Required(AllowEmptyStrings = false)]
    string RedditHost,
    [property: Required(AllowEmptyStrings = false)]
    string RedditApiHost,
    [property: Required] int PostLimit,
    [property: Required(AllowEmptyStrings = false)]
    string UserAgent,
    [property: Required] string[] Subreddits,
    [property: Required(AllowEmptyStrings = false)] 
    string RedditErrorMessage
);
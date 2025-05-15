using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using DecembristChatBotSharp.Entity;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace DecembristChatBotSharp;

public record AppConfig(
    HttpConfig HttpConfig,
    [property: Required(AllowEmptyStrings = false)]
    string TelegramBotToken,
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DatabaseFile,
    int CheckCaptchaIntervalSeconds,
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
    MenuConfig MenuConfig,
    LoreConfig LoreConfig,
    LoreListConfig LoreListConfig,
    RedditConfig RedditConfig,
    RestrictConfig RestrictConfig,
    CurseConfig CurseConfig,
    DislikeConfig DislikeConfig,
    CharmConfig CharmConfig,
    AmuletConfig amuletConfig,
    ItemConfig ItemConfig,
    PollPaymentConfig? PollPaymentConfig,
    KeycloakConfig? KeycloakConfig = null,
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
            .Do(ValidateRecursively)
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to read appsettings.json");
                throw ex;
            });
    }

    private static void ValidateRecursively(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        Validator.ValidateObject(
            obj,
            new ValidationContext(obj),
            validateAllProperties: true
        );

        var props = obj.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var prop in props)
        {
            var value = prop.GetValue(obj);
            if (value.IsNull()) continue;

            if (value is IEnumerable col && !(value is string))
            {
                foreach (var element in col)
                {
                    ValidateRecursively(element);
                }
            }

            else if (!prop.PropertyType.IsValueType && prop.PropertyType != typeof(string))
            {
                ValidateRecursively(value);
            }
        }
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
    int ConnectionCheckTimeoutSeconds
);

public record CommandConfig(
    int CommandIntervalSeconds,
    LikeConfig LikeConfig,
    BanConfig BanConfig,
    TelegramPostConfig TelegramPostConfig,
    PremiumConfig PremiumConfig,
    [property: Required(AllowEmptyStrings = false)]
    string CommandNotReady,
    [property: Required(AllowEmptyStrings = false)]
    string AdminOnlyMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyHelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string InviteToDirectMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StickerNotFoundMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NewFastReplyMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyDuplicateMessage,
    [property: Required(AllowEmptyStrings = false)]
    string WrongCommandMessage,
    [property: Range(1, int.MaxValue)] int FastReplyDaysDuration
);

public record MenuConfig(
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ChatNotAllowed,
    [property: Required(AllowEmptyStrings = false)]
    string ProfileTitle,
    [property: Required(AllowEmptyStrings = false)]
    string LorDescription
);

public record LoreConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ChatTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string EditTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string ChatFailed,
    [property: Required(AllowEmptyStrings = false)]
    string LoreNotFound,
    [property: Required(AllowEmptyStrings = false)]
    string PrivateLoreNotFound,
    [property: Required(AllowEmptyStrings = false)]
    string KeyRequest,
    [property: Required(AllowEmptyStrings = false)]
    string DeleteRequest,
    [property: Required(AllowEmptyStrings = false)]
    string MessageExpired,
    [property: Required(AllowEmptyStrings = false)]
    string DeleteSuccess,
    [property: Required(AllowEmptyStrings = false)]
    string LoreHelp,
    [property: Required(AllowEmptyStrings = false)]
    string ContentSuccess,
    [property: Required(AllowEmptyStrings = false)]
    string ContentDefault,
    [property: Required(AllowEmptyStrings = false)]
    string ContentRequest,
    [property: Required(AllowEmptyStrings = false)]
    string KeyNotFound,
    [property: Required(AllowEmptyStrings = false)]
    string NotLoreUser,
    [property: Required(AllowEmptyStrings = false)]
    string Tip,
    [property: Required(AllowEmptyStrings = false)]
    string PrivateFailed,
    [property: Range(1, int.MaxValue)] int ContentEditExpiration,
    [property: Range(1, int.MaxValue)] int DeleteExpiration,
    [property: Range(1, int.MaxValue)] int ChatLoreExpiration,
    [property: Range(1, int.MaxValue)] int ContentLimit,
    [property: Range(1, int.MaxValue)] int KeyLimit
);

public record LoreListConfig(
    [property: Required(AllowEmptyStrings = false)]
    string NotFound,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string NotAccess,
    [property: Range(1, int.MaxValue)] int RowLimit,
    [property: Range(1, int.MaxValue)] int ExpirationMinutes
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

public record BanConfig(
    [property: Required(AllowEmptyStrings = false)]
    string BanMessage,
    [property: Required(AllowEmptyStrings = false)]
    string BanNoReasonMessage,
    [property: Required(AllowEmptyStrings = false)]
    string BanAmuletMessage,
    [property: Required(AllowEmptyStrings = false)]
    string BanReceiverNotSetMessage,
    [property: Required(AllowEmptyStrings = false)]
    string BanAdditionMessage,
    int ReasonLengthLimit,
    [property: Required(AllowEmptyStrings = false)]
    string ReasonLengthErrorMessage
);

public record RestrictConfig(
    [property: Required(AllowEmptyStrings = false)]
    string RestrictMessage,
    [property: Required(AllowEmptyStrings = false)]
    string RestrictClearMessage
);

public record CurseConfig(
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DuplicateMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSetMessage,
    [property: Range(1, int.MaxValue)] int DurationMinutes
);

public record DislikeConfig(
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ExistDislikeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSetMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SelfMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DailyResultMessage,
    [property: Required(AllowEmptyStrings = false)]
    string CurseSuccess,
    [property: Required(AllowEmptyStrings = false)]
    string CurseBlocked,
    [property: Required(AllowEmptyStrings = false)]
    string DailyResultCronUtc,
    [property: Required(AllowEmptyStrings = false)]
    string DailyResultEmoji,
    [property: Range(1, int.MaxValue)] int EmojiDurationMinutes
);

public record CharmConfig(
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSetMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SelfMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DuplicateMessage,
    [property: Range(1, int.MaxValue)] int CharacterLimit,
    [property: Range(1, int.MaxValue)] int DurationMinutes
);

public record AmuletConfig(
    [property: Required(AllowEmptyStrings = false)]
    string AmuletBreaksMessage,
    [property: Required(AllowEmptyStrings = false)]
    int MessageExpirationMinutes);

public record ItemConfig(
    Dictionary<MemberItemType, double> ItemChance,
    [property: Required(AllowEmptyStrings = false)]
    string NoItemsMessage,
    [property: Required(AllowEmptyStrings = false)]
    string GetItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MultipleItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AmuletBrokenMessage,
    [property: Required(AllowEmptyStrings = false)]
    string EmptyInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string InviteInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedToOpenBoxMessage
);

public record LikeConfig(
    int TopLikeMemberCount,
    [property: Required(AllowEmptyStrings = false)]
    string LikeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LikeReceiverNotSet,
    [property: Required(AllowEmptyStrings = false)]
    string SelfLikeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NoLikesMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DailyTopLikersGiftCronUtc,
    [property: Required(AllowEmptyStrings = false)]
    int DailyTopLikersCount,
    [property: Required(AllowEmptyStrings = false)]
    string TopLikersGiftMessage
);

public record TelegramPostConfig(
    string[] ChannelNames,
    [property: Range(1, int.MaxValue)] int ScanPostCount,
    [property: Range(1, int.MaxValue)] int MaxGetPostRetries,
    [property: Required(AllowEmptyStrings = false)]
    string TelegramErrorMessage
);

public record PremiumConfig(
    [property: Required(AllowEmptyStrings = false)]
    string AddPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string UpdatePremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string RemovePremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NotPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ImPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DailyPremiumRewardCronUtc,
    [property: Required(AllowEmptyStrings = false)]
    string DailyPremiumRewardMessage
);

public record HttpConfig(
    [property: Required(AllowEmptyStrings = false)]
    int Port,
    [property: Required(AllowEmptyStrings = false)]
    string Host
);

public record PollPaymentConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ServiceUrl,
    [property: Required] int PollIntervalSeconds,
    [property: Required]
    [property: MinLength(1)]
    System.Collections.Generic.HashSet<ProductListItem>? ProductList
);

public record ProductListItem(string Regex, ProductType Type);

public record KeycloakConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ServerUrl,
    [property: Required(AllowEmptyStrings = false)]
    string Realm,
    [property: Required(AllowEmptyStrings = false)]
    string ClientId,
    [property: Required(AllowEmptyStrings = false)]
    string ClientSecret
);
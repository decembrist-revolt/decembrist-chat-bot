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
    string DatabaseFile,
    int UpdateExpirationSeconds,
    CaptchaConfig CaptchaConfig,
    AllowedChatConfig AllowedChatConfig,
    MongoConfig MongoConfig,
    CommandConfig CommandConfig,
    MenuConfig MenuConfig,
    LoreConfig LoreConfig,
    ListConfig ListConfig,
    FilterConfig FilterConfig,
    RedditConfig RedditConfig,
    RestrictConfig RestrictConfig,
    CurseConfig CurseConfig,
    MinaConfig MinaConfig,
    SlotMachineConfig SlotMachineConfig,
    DislikeConfig DislikeConfig,
    CharmConfig CharmConfig,
    AmuletConfig AmuletConfig,
    ItemConfig ItemConfig,
    HelpConfig HelpConfig,
    GiveConfig GiveConfig,
    DustConfig DustConfig,
    CraftConfig CraftConfig,
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
            .AddJsonFile("craftsettings.json", optional: false, reloadOnChange: true)
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

public record CaptchaConfig(
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Range(1, int.MaxValue)] int WelcomeMessageExpiration,
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaAnswer,
    [property: Required(AllowEmptyStrings = false)]
    string JoinText,
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaRequestAgainText,
    [property: Range(1, int.MaxValue)] int CheckCaptchaIntervalHours,
    [property: Range(1, long.MaxValue)] long CaptchaTimeHours,
    [property: Range(1, int.MaxValue)] int CaptchaRequestAgainCount,
    [property: Range(1, int.MaxValue)] int CaptchaRequestAgainExpiration,
    [property: Range(1, int.MaxValue)] int CaptchaRetryCount);

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
    [property: Range(1, int.MaxValue)] int CommandIntervalSeconds,
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
    string FastReplyBlockedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string WrongCommandMessage,
    [property: Range(1, int.MaxValue)] int FastReplyDaysDuration,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyExpiredMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyCheckExpireCronUtc
);

public record MenuConfig(
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ChatNotAllowed,
    [property: Required(AllowEmptyStrings = false)]
    string ProfileTitle,
    [property: Required(AllowEmptyStrings = false)]
    string LoreDescription,
    [property: Required(AllowEmptyStrings = false)]
    string FilterDescription
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

public record ListConfig(
    [property: Required(AllowEmptyStrings = false)]
    string NotFound,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NotAccess,
    [property: Range(1, int.MaxValue)] int RowLimit,
    [property: Range(1, int.MaxValue)] int ExpirationMinutes
);

public record FilterConfig(
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaMessage,
    [property: Required(AllowEmptyStrings = false)]
    string CaptchaAnswer,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessAddMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedAddMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DuplicateMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ExpiredMessage,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string CreateRequest,
    [property: Required(AllowEmptyStrings = false)]
    string DeleteRequest,
    [property: Required(AllowEmptyStrings = false)]
    string DeleteSuccess,
    [property: Required(AllowEmptyStrings = false)]
    string NotFound,
    [property: Range(1, int.MaxValue)] int CheckCaptchaIntervalSeconds,
    [property: Range(1, int.MaxValue)] int CaptchaTimeSeconds,
    [property: Range(1, int.MaxValue)] int ExpiredAddMinutes
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

public record MinaConfig(
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DuplicateMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ActivationMessage,
    [property: Range(1, int.MaxValue)] int DurationMinutes,
    [property: Range(1, int.MaxValue)] int TriggerMaxLength
);

public record SlotMachineConfig(
    [property: Required(AllowEmptyStrings = false)]
    string LaunchMessage,
    [property: Required(AllowEmptyStrings = false)]
    string WinMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LoseMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ErrorMessage,
    [property: Required(AllowEmptyStrings = false)]
    string Premium777Message,
    [property: Range(1, int.MaxValue)] int PremiumAttempts,
    [property: Range(1, int.MaxValue)] int PremiumDaysFor777
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
    string AmuletDescription,
    [property: Required(AllowEmptyStrings = false)]
    int MessageExpirationMinutes);

public record ItemDropConfig(
    double Chance,
    int Quantity = 1
);

public record ItemConfig(
    Dictionary<MemberItemType, ItemDropConfig> ItemChance,
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
    string FailedToOpenBoxMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StoneDescription,
    MemberItemType CompensationItem,
    [property: Range(1, int.MaxValue)] int UniqueItemGiveExpirationMinutes,
    [property: Range(1, int.MaxValue)] int BoxMessageExpiration
);

public record GiveConfig(
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AdminSuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string GiveNotExpiredMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SelfMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSet,
    [property: Range(1, int.MaxValue)] int ExpirationMinutes);

public record HelpConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ItemHelpTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string CommandHelpTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string HelpTitle,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage
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

public record CraftConfig(
    IReadOnlyList<CraftRecipe> Recipes,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string PremiumSuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NoRecipeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage,
    [property: Range(1, int.MaxValue)] int PremiumBonus,
    [property: Range(1, int.MaxValue)] int SuccessExpiration,
    [property: Range(0, 1)] double PremiumChance
);

public record DustConfig(
    IReadOnlyDictionary<MemberItemType, DustRecipe> DustRecipes,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string PremiumSuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NoRecipeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string GreenDustDescription,
    [property: Required(AllowEmptyStrings = false)]
    string DustDescription,
    [property: Range(1, int.MaxValue)] int SuccessExpiration
);

public record CraftRecipe(
    List<ItemQuantity> Inputs,
    List<OutputItem> Outputs
);

public record OutputItem(
    MemberItemType Item,
    double Chance,
    int Quantity = 1
);

public record ItemQuantity(
    MemberItemType Item,
    int Quantity = 1
);

public record DustRecipe(
    DustReward Reward,
    PremiumReward? PremiumReward = null
);

public record QuantityRange(int Min, int Max);

public record DustReward(
    MemberItemType Item,
    QuantityRange Range);

public record PremiumReward(
    MemberItemType Item,
    double Chance,
    int Quantity);
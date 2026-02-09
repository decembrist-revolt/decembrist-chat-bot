using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Entity.Configs;
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
    CaptchaJobConfig CaptchaJobConfig,
    AllowedChatConfig AllowedChatConfig,
    MongoConfig MongoConfig,
    GlobalAdminConfig GlobalAdminConfig,
    CommandAssistanceConfig CommandAssistanceConfig,
    LoreServiceConfig LoreServiceConfig,
    FilterJobConfig FilterJobConfig,
    RedditConfig RedditConfig,
    DislikeJobConfig DislikeJobConfig,
    AmuletConfig AmuletConfig,
    ItemAssistanceConfig ItemAssistanceConfig,
    DustRecipesConfig DustRecipesConfig,
    CraftRecipesConfig CraftRecipesConfig,
    PollPaymentConfig? PollPaymentConfig,
    MinionConfig MinionConfig,
    ChatConfig ChatConfigTemplate,
    ChatConfigMessages ChatConfigMessages,
    QuizConfig? QuizConfig = null,
    DeepSeekConfig? DeepSeekConfig = null,
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
            .AddJsonFile("chatConfigTemplate.json", optional: false, reloadOnChange: true)
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
    [property: Required(AllowEmptyStrings = false)]
    string WrongChatText,
    [property: Required(AllowEmptyStrings = false)]
    string RightChatText,
    System.Collections.Generic.HashSet<long>? AllowedChatIds = null
);

public record CaptchaJobConfig(
    [property: Range(1, int.MaxValue)] int CheckCaptchaIntervalSeconds,
    [property: Range(1, long.MaxValue)] long CaptchaTimeSeconds,
    [property: Range(1, int.MaxValue)] int CaptchaRequestAgainCount,
    [property: Range(1, int.MaxValue)] int CaptchaRetryCount);

public record MongoConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ConnectionString,
    int ConnectionCheckTimeoutSeconds
);

public record GlobalAdminConfig(
    System.Collections.Generic.HashSet<long> AdminIds
);

public record CommandAssistanceConfig(
    [property: Range(1, int.MaxValue)] int CommandIntervalSeconds,
    LikeJobConfig LikeJobConfig,
    PremiumConfig PremiumConfig,
    [property: Required(AllowEmptyStrings = false)]
    string CommandNotReady,
    [property: Required(AllowEmptyStrings = false)]
    string AdminOnlyMessage,
    [property: Required(AllowEmptyStrings = false)]
    string InviteToDirectMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StickerNotFoundMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyExpiredMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FastReplyCheckExpireCronUtc,
    Dictionary<string, string> CommandDescriptions
);

public record LoreServiceConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ChatTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string ChatFailed,
    [property: Required(AllowEmptyStrings = false)]
    string Tip,
    [property: Range(1, int.MaxValue)] int ContentEditExpiration,
    [property: Range(1, int.MaxValue)] int DeleteExpiration,
    [property: Range(1, int.MaxValue)] int ContentLimit,
    [property: Range(1, int.MaxValue)] int KeyLimit
);

public record FilterJobConfig(
    [property: Range(1, int.MaxValue)] int CheckCaptchaIntervalSeconds,
    [property: Range(1, int.MaxValue)] int CaptchaTimeSeconds
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

public record DislikeJobConfig(
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

public record AmuletConfig(
    [property: Required(AllowEmptyStrings = false)]
    string AmuletBreaksMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AmuletDescription,
    [property: Required(AllowEmptyStrings = false)]
    int MessageExpirationMinutes);

public record MinionConfig(
    [property: Required(AllowEmptyStrings = false)]
    string MinionConfirmationPrefix,
    [property: Required(AllowEmptyStrings = false)]
    string InvitationMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MinionCreatedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AlreadyHasMinionMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AlreadyHasInvitationMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AlreadyIsMinionMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NotPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string TargetIsPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSetMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ShowMinionStatusMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NegativeEffectRedirectMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MinionRevokedByPremiumLossMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MinionRevokedByBecomingPremiumMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MinionRevokedByDeleteMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AmuletTransferMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StoneTransferMessage,
    [property: Range(1, int.MaxValue)] int InvitationExpirationMinutes,
    [property: Range(1, int.MaxValue)] int MessageExpirationMinutes,
    [property: Required(AllowEmptyStrings = false)]
    string ConfirmationCheckCron,
    [property: Range(0.0, 1.0)] double DailyBoxChance
);

public record ItemDropConfig(
    double Chance,
    int Quantity = 1
);

public record ItemAssistanceConfig(
    [property: Required(AllowEmptyStrings = false)]
    string NoItemsMessage,
    [property: Required(AllowEmptyStrings = false)]
    string GetItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MultipleItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AmuletBrokenMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StoneDescription,
    [property: Range(1, int.MaxValue)] int BoxMessageExpiration
);

public record LikeJobConfig(
    [property: Required(AllowEmptyStrings = false)]
    string DailyTopLikersGiftCronUtc,
    [property: Required(AllowEmptyStrings = false)]
    int DailyTopLikersCount,
    [property: Required(AllowEmptyStrings = false)]
    string TopLikersGiftMessage
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
    string DailyPremiumRewardMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DailyMinionRewardMessage
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

public record DeepSeekConfig(
    bool Enabled,
    [property: Required(AllowEmptyStrings = false)]
    string ApiUrl,
    [property: Required(AllowEmptyStrings = false)]
    string BearerToken,
    [property: Required(AllowEmptyStrings = false)]
    string ThinkingMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NoTokensMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedToUseTokenMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AiErrorMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AiTokenDescription,
    [property: Required(AllowEmptyStrings = false)]
    string ActiveQuizMessage
);

public record CraftRecipesConfig(
    IReadOnlyList<CraftRecipe> Recipes,
    [property: Range(0, 1)] double PremiumChance
);

public record DustRecipesConfig(
    IReadOnlyDictionary<MemberItemType, DustRecipe> DustRecipes,
    [property: Required(AllowEmptyStrings = false)]
    string GreenDustDescription,
    [property: Required(AllowEmptyStrings = false)]
    string DustDescription
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

public record QuizConfig(
    bool Enabled,
    [property: Required(AllowEmptyStrings = false)]
    string QuestionGenerationCronUtc,
    IReadOnlyList<string> Topics,
    [property: Required(AllowEmptyStrings = false)]
    string QuestionGenerationPrompt,
    [property: Required(AllowEmptyStrings = false)]
    string AnswerValidationPrompt,
    [property: Required(AllowEmptyStrings = false)]
    string BatchAnswerValidationPrompt,
    [property: Required(AllowEmptyStrings = false)]
    string QuestionMessageTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string WinnerMessageTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string QuizCompletedTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string QuizUnansweredTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string SubtopicAvoidancePrompt,
    [property: Range(1, int.MaxValue)] int AutoCloseUnansweredMinutes = 240,
    [property: Range(1, int.MaxValue)] int SubtopicHistoryLimit = 25
);

public record ChatConfigMessages(
    [property: Required(AllowEmptyStrings = false)]
    string AdminOnlyMessage,
    [property: Required(AllowEmptyStrings = false)]
    string InvalidIdMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ToggleFailedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AlreadyExistsMessage,
    [property: Required(AllowEmptyStrings = false)]
    string TemplateErrorMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessAddMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedAddMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessDeleteMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NotFoundMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ToggleSuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AddConfigRequest,
    [property: Required(AllowEmptyStrings = false)]
    string EnableConfigRequest,
    [property: Required(AllowEmptyStrings = false)]
    string DisableConfigRequest,
    [property: Required(AllowEmptyStrings = false)]
    string DeleteConfigRequest,
    [property: Required(AllowEmptyStrings = false)]
    string ChatListMessage
);
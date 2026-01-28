using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity.Configs;

public interface IConfig
{
    bool Enabled { get; init; }
}

public record ChatConfig(
    [property: BsonId] long ChatId,
    CaptchaConfig CaptchaConfig,
    CommandConfig CommandConfig,
    LikeConfig LikeConfig,
    BanConfig BanConfig,
    TelegramPostConfig TelegramPostConfig,
    ProfileConfig ProfileConfig,
    LoreConfig LoreConfig,
    ListConfig ListConfig,
    FilterConfig FilterConfig,
    RestrictConfig RestrictConfig,
    CurseConfig CurseConfig,
    MinaConfig MinaConfig,
    SlotMachineConfig SlotMachineConfig,
    DislikeConfig DislikeConfig,
    CharmConfig CharmConfig,
    ItemConfig ItemConfig,
    HelpConfig HelpConfig,
    GiveConfig GiveConfig,
    GiveawayConfig GiveawayConfig,
    DustConfig DustConfig,
    CraftConfig CraftConfig,
    MazeConfig MazeConfig
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
    bool Enabled = false
) : IConfig;

public record CurseConfig(
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string DuplicateMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ReceiverNotSetMessage,
    [property: Range(1, int.MaxValue)] int DurationMinutes,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int DurationMinutes,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int CaptchaRetryCount,
    bool Enabled = false) : IConfig;

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
    string ReasonLengthErrorMessage,
    bool Enabled = false
) : IConfig;

public record TelegramPostConfig(
    string[] ChannelNames,
    [property: Range(1, int.MaxValue)] int ScanPostCount,
    [property: Range(1, int.MaxValue)] int MaxGetPostRetries,
    [property: Required(AllowEmptyStrings = false)]
    string TelegramErrorMessage,
    bool Enabled = false
) : IConfig;

public record CommandConfig(
    [property: Range(1, int.MaxValue)] int CommandIntervalSeconds,
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
    string FastReplyCheckExpireCronUtc,
    bool Enabled = false
) : IConfig;

public record ProfileConfig(
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ChatNotAllowed,
    [property: Required(AllowEmptyStrings = false)]
    string ProfileTitle,
    [property: Required(AllowEmptyStrings = false)]
    string LoreDescription,
    [property: Required(AllowEmptyStrings = false)]
    string FilterDescription,
    [property: Required(AllowEmptyStrings = false)]
    string NonMazeDescription,
    [property: Required(AllowEmptyStrings = false)]
    string MazeDescription,
    bool Enabled = false
) : IConfig;

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
    [property: Required(AllowEmptyStrings = false)]
    string MessageExpired,
    [property: Range(1, int.MaxValue)] int ContentEditExpiration,
    [property: Range(1, int.MaxValue)] int DeleteExpiration,
    [property: Range(1, int.MaxValue)] int ChatLoreExpiration,
    [property: Range(1, int.MaxValue)] int ContentLimit,
    [property: Range(1, int.MaxValue)] int KeyLimit,
    bool Enabled = false
) : IConfig;

public record ListConfig(
    [property: Required(AllowEmptyStrings = false)]
    string NotFound,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NotAccess,
    [property: Range(1, int.MaxValue)] int ExpirationMinutes,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int ExpiredAddMinutes,
    bool Enabled = false
) : IConfig;

public record RestrictConfig(
    [property: Required(AllowEmptyStrings = false)]
    string RestrictClearMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LinkRestrictMessage,
    [property: Required(AllowEmptyStrings = false)]
    string TimeoutRestrictMessage,
    [property: Required(AllowEmptyStrings = false)]
    string CombinedRestrictMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LinkShortName,
    [property: Required(AllowEmptyStrings = false)]
    string TimeoutShortName,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int TriggerMaxLength,
    bool Enabled = false
) : IConfig;

public record SlotMachineConfig(
    [property: Required(AllowEmptyStrings = false)]
    string LaunchMessage,
    [property: Required(AllowEmptyStrings = false)]
    string WinMessage,
    [property: Required(AllowEmptyStrings = false)]
    string LoseMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ErrorMessage,
    [property: Range(1, int.MaxValue)] int PremiumAttempts,
    [property: Range(1, int.MaxValue)] int PremiumDaysFor777,
    [property: Required(AllowEmptyStrings = false)]
    string Premium777Message,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int EmojiDurationMinutes,
    bool Enabled = false
) : IConfig;

public record ItemConfig(
    Dictionary<MemberItemType, ItemDropConfig> ItemChance,
    [property: Required(AllowEmptyStrings = false)]
    string AmuletBrokenMessage,
    [property: Required(AllowEmptyStrings = false)]
    string NoItemsMessage,
    [property: Required(AllowEmptyStrings = false)]
    string GetItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string MultipleItemMessage,
    [property: Required(AllowEmptyStrings = false)]
    string EmptyInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string InviteInventoryMessage,
    [property: Required(AllowEmptyStrings = false)]
    string FailedToOpenBoxMessage,
    [property: Required(AllowEmptyStrings = false)]
    string StoneDescription,
    MemberItemType CompensationItem,
    [property: Range(1, int.MaxValue)] int UniqueItemGiveExpirationMinutes,
    [property: Range(1, int.MaxValue)] int BoxMessageExpiration,
    bool Enabled = false
) : IConfig;

public record HelpConfig(
    [property: Required(AllowEmptyStrings = false)]
    string ItemHelpTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string CommandHelpTemplate,
    [property: Required(AllowEmptyStrings = false)]
    string HelpTitle,
    [property: Required(AllowEmptyStrings = false)]
    string FailedMessage,
    bool Enabled = false
) : IConfig;

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
    [property: Range(1, int.MaxValue)] int ExpirationMinutes,
    bool Enabled = false
) : IConfig;

public record GiveawayConfig(
    [property: Required(AllowEmptyStrings = false)]
    string AnnouncementMessage,
    [property: Required(AllowEmptyStrings = false)]
    string SuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string PublicSuccessMessage,
    [property: Required(AllowEmptyStrings = false)]
    string AlreadyReceivedMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ErrorMessage,
    [property: Required(AllowEmptyStrings = false)]
    string HelpMessage,
    [property: Required(AllowEmptyStrings = false)]
    string ButtonText,
    [property: Range(1, int.MaxValue)] int DefaultDurationMinutes,
    bool Enabled = false
) : IConfig;

public record DustConfig(
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
    [property: Range(1, int.MaxValue)] int SuccessExpiration,
    bool Enabled = false
) : IConfig;

public record CraftConfig(
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
    [property: Range(0, 1)] double PremiumChance,
    bool Enabled = false
) : IConfig;

public record MazeConfig(
    [property: Range(1, int.MaxValue)] int ChestFrequency = 50,
    [property: Range(1, 10)] int DefaultViewRadius = 3,
    [property: Range(0, int.MaxValue)] int MoveDelaySeconds = 3,
    [property: Range(16, 512)] int MazeSize = 128,
    [property: Range(1, int.MaxValue)] int WinnerBoxReward = 5,
    [property: Required(AllowEmptyStrings = false)]
    string InventoryTextTemplate = "",
    [property: Required(AllowEmptyStrings = false)]
    string WelcomeMessage = "",
    [property: Required(AllowEmptyStrings = false)]
    string RepeatAnnouncementMessage = "",
    [property: Required(AllowEmptyStrings = false)]
    string GameNotFoundMessage = "",
    [property: Required(AllowEmptyStrings = false)]
    string GameExitMessage = "",
    [property: Required(AllowEmptyStrings = false)]
    string KeyboardIncorrectMessage = "",
    [property: Required(AllowEmptyStrings = false)]
    string AnnouncementMessage = "",
    bool Enabled = false
) : IConfig;
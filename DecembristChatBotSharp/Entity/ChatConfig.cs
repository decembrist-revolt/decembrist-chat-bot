using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;

namespace DecembristChatBotSharp.Entity;

public record ChatConfig(
    [property: BsonId] long ChatId,
    int UpdateExpirationSeconds,
    CaptchaConfig2 CaptchaConfig,
    CommandConfig CommandConfig,
    MenuConfig MenuConfig,
    LoreConfig LoreConfig,
    ListConfig ListConfig,
    FilterConfig FilterConfig,
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
    GiveawayConfig GiveawayConfig,
    DustConfig DustConfig,
    CraftConfig CraftConfig,
    MazeConfig MazeConfig,
    QuizConfig? QuizConfig = null,
    DeepSeekConfig? DeepSeekConfig = null,
    List<long>? WhiteListIds = null
);

public record CaptchaConfig2(
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
    bool IsEnabled = false);
using DecembristChatBotSharp.Entity;

namespace DecembristChatBotSharp.Items;

public interface IPassiveItem
{
    public MemberItemType ItemType { get; }
    public string Description { get; }
}

public class Amulet(AppConfig appConfig) : IPassiveItem
{
    public MemberItemType ItemType => MemberItemType.Amulet;
    public string Description => appConfig.AmuletConfig.AmuletDescription;
}

public class GreenDust(AppConfig appConfig) : IPassiveItem
{
    public MemberItemType ItemType => MemberItemType.GreenDust;
    public string Description => appConfig.DustConfig.GreenDustDescription;
}

public class BlueDust(AppConfig appConfig) : IPassiveItem
{
    public MemberItemType ItemType => MemberItemType.BlueDust;
    public string Description => appConfig.DustConfig.DustDescription;
}

public class RedDust(AppConfig appConfig) : IPassiveItem
{
    public MemberItemType ItemType => MemberItemType.RedDust;
    public string Description => appConfig.DustConfig.DustDescription;
}

public class Stone(AppConfig appConfig) : IPassiveItem
{
    public MemberItemType ItemType => MemberItemType.Stone;
    public string Description => appConfig.ItemConfig.StoneDescription;
}
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ChatConfigService(ChatConfigRepository db, AppConfig appConfig)
{
    public async Task<Option<ChatConfig>> GetChatConfig(long chatId) => await db.GetChatConfig(chatId);

    public async Task<Option<T>> GetConfig<T>(long chatId, Expression<Func<ChatConfig, T>> selector)
        where T : IConfig => (await db.GetSpecificConfig(chatId, selector)).Filter(x => x.Enabled);

    public async Task<bool> InsertChatConfig(ChatConfig config) => await db.InsertChatConfig(config);

    public async Task<bool> DeleteChatConfig(long chatId) => await db.DeleteChatConfig(chatId);

    public T LogNonExistConfig<T>(T input, string configName, [CallerMemberName] string? callerName = null)
    {
        Log.Information("{memberName}: Config disabled or not found: {configName}, skipping lock acquisition",
            callerName, configName);
        return input;
    }

    public ChatConfig GetNewConfig(long chatId, bool enabled)
    {
        var template = appConfig.ChatConfigTemplate;
        return new ChatConfig(
            ChatId: chatId,
            CaptchaConfig: template.CaptchaConfig with { Enabled = true },
            CommandConfig: template.CommandConfig with { Enabled = enabled },
            LikeConfig: template.LikeConfig with { Enabled = enabled },
            BanConfig: template.BanConfig with { Enabled = enabled },
            TelegramPostConfig: template.TelegramPostConfig with { Enabled = enabled },
            ProfileConfig: template.ProfileConfig with { Enabled = enabled },
            LoreConfig: template.LoreConfig with { Enabled = enabled },
            ListConfig: template.ListConfig with { Enabled = enabled },
            FilterConfig: template.FilterConfig with { Enabled = enabled },
            RestrictConfig: template.RestrictConfig with { Enabled = enabled },
            CurseConfig: template.CurseConfig with { Enabled = enabled },
            MinaConfig: template.MinaConfig with { Enabled = enabled },
            SlotMachineConfig: template.SlotMachineConfig with { Enabled = enabled },
            DislikeConfig: template.DislikeConfig with { Enabled = enabled },
            CharmConfig: template.CharmConfig with { Enabled = enabled },
            ItemConfig: template.ItemConfig with { Enabled = enabled },
            HelpConfig: template.HelpConfig with { Enabled = enabled },
            GiveConfig: template.GiveConfig with { Enabled = enabled },
            GiveawayConfig: template.GiveawayConfig with { Enabled = enabled },
            DustConfig: template.DustConfig with { Enabled = enabled },
            CraftConfig: template.CraftConfig with { Enabled = enabled },
            MazeConfig: template.MazeConfig with { Enabled = enabled }
        );
    }
}
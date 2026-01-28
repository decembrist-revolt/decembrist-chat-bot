using System.Runtime.CompilerServices;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ChatConfigService(
    ChatConfigRepository db
)
{
    public async Task<Option<ChatConfig>> GetChatConfig(long chatId) => await db.GetChatConfig(chatId);

    public async Task<Option<T>> GetConfig<T>(long chatId, Func<ChatConfig, T> selector) where T : IConfig =>
        (await db.GetChatConfig(chatId))
        .Map(selector)
        .Filter(x => x.Enabled);

    public Option<T> GetConfig<T>(Option<ChatConfig> config, Func<ChatConfig, T> selector)
        where T : IConfig => config.Map(selector).Filter(x => x.Enabled);

    public T LogNonExistConfig<T>(T input, string configName, [CallerMemberName] string? callerName = null)
    {
        Log.Information("{memberName}: Config disabled or not found: {configName}, skipping lock acquisition",
            callerName, configName);
        return input;
    }
}
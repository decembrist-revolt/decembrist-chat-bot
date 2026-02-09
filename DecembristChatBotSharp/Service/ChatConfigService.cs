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

    public T LogNonExistConfig<T>(T input, string configName, [CallerMemberName] string? callerName = null)
    {
        Log.Information("{memberName}: Config disabled or not found: {configName}, skipping lock acquisition",
            callerName, configName);
        return input;
    }
}
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Microsoft.Extensions.Caching.Memory;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ChatConfigService(
    ChatConfigRepository db,
    IMemoryCache cache)
{
    private const string CacheKeyPrefix = "ChatConfig_";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(30);

    public async Task<Option<ChatConfig>> GetChatConfig(long chatId)
    {
        var cacheKey = CacheKeyPrefix + chatId;

        if (cache.TryGetValue(cacheKey, out ChatConfig? cachedConfig))
        {
            return cachedConfig;
        }

        var configOption = await db.GetChatConfig(chatId);
        configOption.Match(
            config => cache.Set(cacheKey, config, CacheExpiration),
            () => { }
        );

        return configOption;
    }

    public async Task<Option<T>> GetConfig<T>(long chatId, Func<ChatConfig, T> selector) where T : IConfig =>
        (await GetChatConfig(chatId))
        .Map(selector)
        .Filter(x => x.Enabled);

    public void InvalidateCache(long chatId)
    {
        var cacheKey = CacheKeyPrefix + chatId;
        cache.Remove(cacheKey);
    }
}
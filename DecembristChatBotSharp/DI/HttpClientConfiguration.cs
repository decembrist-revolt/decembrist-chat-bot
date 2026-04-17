using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace DecembristChatBotSharp.DI;

public static class HttpClientConfiguration
{
    public const string RedditClient = nameof(RedditClient);
    public const string DeepSeekClient = nameof(DeepSeekClient);
    public const string TelegramMemeClient = nameof(TelegramMemeClient);
    
    public static void AddHttpClients(this ServiceRegistry registry, AppConfig appConfig)
    {
        registry.AddHttpClient(RedditClient, client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", appConfig.RedditConfig.UserAgent);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        
        registry.AddHttpClient(DeepSeekClient, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });
        
        registry.AddHttpClient(TelegramMemeClient, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
    }
}
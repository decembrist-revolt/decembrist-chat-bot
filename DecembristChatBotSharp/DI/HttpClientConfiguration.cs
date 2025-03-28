using Lamar;
using Microsoft.Extensions.DependencyInjection;

namespace DecembristChatBotSharp.DI;

public static class HttpClientConfiguration
{
    public const string RedditClient = nameof(RedditClient);
    
    public static void AddHttpClients(this ServiceRegistry registry, AppConfig appConfig)
    {
        registry.AddHttpClient(RedditClient, client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", appConfig.RedditConfig.UserAgent);
        });
    }
}
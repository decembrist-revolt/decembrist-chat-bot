using System.Text.Json;
using System.Text.Json.Serialization;
using Lamar;
using Serilog;
using static System.Net.HttpStatusCode;

namespace DecembristChatBotSharp.Reddit;

internal record RedditResponse(
    [property: JsonPropertyName("data")] RedditResponse.Data ResponseData
)
{
    public record Data(
        [property: JsonPropertyName("children")]
        Child[] Children
    );

    public record Child(
        [property: JsonPropertyName("data")] ChildData Data
    );

    public record ChildData(
        [property: JsonPropertyName("permalink")]
        string Permalink,
        [property: JsonPropertyName("url")] string Url
    );
}

internal record RedditAuthResponse(
    [property: JsonPropertyName("access_token")]
    string AccessToken
);

public record RedditRandomMeme(string Permalink, string Url);

[Singleton]
public class RedditService(AppConfig appConfig)
{
    private const string AuthUrl = "/api/v1/access_token";
    private const string Bearer = nameof(Bearer);

    private readonly HttpClient _client = new HttpClient().Apply(client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent", appConfig.RedditConfig.UserAgent);
        return client;
    });

    private bool _isAuthenticated = false;

    public async Task<Option<RedditRandomMeme>> GetRandomMeme()
    {
        if (!_isAuthenticated && !await Authenticate())
        {
            _isAuthenticated = false;
            return None;
        }

        var redditConfig = appConfig.RedditConfig;

        var maybeResponse = await GetPosts(redditConfig);

        var maybeContent = await TryGetContent<RedditResponse>(maybeResponse);

        if (maybeContent.IsNone)
        {
            Log.Error("Error while deserializing Reddit API response");
            return None;
        }

        var posts = maybeContent
            .Map(response => response.ResponseData.Children)
            .Sequence()
            .Flatten()
            .ToArr();
        
        if (posts.IsEmpty)
        {
            Log.Error("No posts found in Reddit API response");
            return None;
        }
        
        var children = posts.Map(post => post.Data)
            .Filter(data => IsUrlImage(data.Url))
            .ToArr();

        if (children.IsEmpty)
        {
            Log.Error("No images found in Reddit API response");
            return None;
        }

        var rand = new Random();
        var randomChild = children[rand.Next(children.Count)];

        return new RedditRandomMeme(
            redditConfig.RedditHost + randomChild.Permalink,
            randomChild.Url);
    }

    private async Task<Option<HttpResponseMessage>> GetPosts(RedditConfig redditConfig)
    {
        var rand = new Random();
        var subreddits = redditConfig.Subreddits;
        var selectedSubreddit = subreddits[rand.Next(subreddits.Length)];
        var url = $"{redditConfig.RedditApiHost}/r/{selectedSubreddit}/hot?limit={redditConfig.PostLimit}";
        return await _client.GetAsync(url)
            .ToTryAsync()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Error while requesting posts Reddit API");
                return None;
            });
    }

    private bool IsUrlImage(string url) =>
        url.EndsWith(".jpg") ||
        url.EndsWith(".jpeg") ||
        url.EndsWith(".png") ||
        url.EndsWith(".gif") ||
        url.Contains("i.redd.it") ||
        url.Contains("i.imgur.com");

    private async Task<bool> Authenticate()
    {
        if (_client.DefaultRequestHeaders.Contains("Authorization"))
        {
            _client.DefaultRequestHeaders.Remove("Authorization");
        }

        var clientId = appConfig.RedditConfig.ClientId;
        var clientSecret = appConfig.RedditConfig.ClientSecret;
        var authBytes = System.Text.Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}");
        var auth = Convert.ToBase64String(authBytes);
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);

        var maybeResponse = await SendAuthRequest();

        if (maybeResponse.IsNone) return false;

        var maybeContent = await TryGetContent<RedditAuthResponse>(maybeResponse);

        if (maybeContent.IsNone)
        {
            Log.Error("Error while deserializing Reddit API response");
            return false;
        }

        maybeContent.Map(content => content.AccessToken).IfSome(accessToken =>
        {
            _client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(Bearer, accessToken);
            _isAuthenticated = true;
        });

        return _isAuthenticated;
    }

    private async Task<Option<T>> TryGetContent<T>(Option<HttpResponseMessage> maybeResponse)
    {
        var tryDeserialize = await maybeResponse.Bind(CheckFail)
            .MapAsync(httpResponseMessage => httpResponseMessage.Content.ReadAsStringAsync())
            .Map(json => Try(() => JsonSerializer.Deserialize<T>(json)))
            .Value;
        
        return tryDeserialize.Match(Optional, ex =>
        {
            Log.Error(ex, "Error while deserializing Reddit API response");
            return None;
        });
    }

    private async Task<Option<HttpResponseMessage>> SendAuthRequest()
    {
        var request = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);
        var redditLink = appConfig.RedditConfig.RedditHost + AuthUrl;
        var maybeResponse = await _client.PostAsync(redditLink, request).ToTryAsync()
            .ToEither()
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Ошибка при запросе аутентификации Reddit API");
                return None;
            });
        return maybeResponse;
    }

    private Option<HttpResponseMessage> CheckFail(HttpResponseMessage message)
    {
        if (message.IsSuccessStatusCode) return Some(message);

        if (message.StatusCode is Unauthorized or Forbidden)
        {
            _isAuthenticated = false;
            Log.Error("Error while authenticating Reddit API: {0}", message.ReasonPhrase);
        }
        else
        {
            Log.Error("Error requesting Reddit API: {0}", message.ReasonPhrase);
        }

        return None;
    }
}
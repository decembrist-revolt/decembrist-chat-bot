using System.Text.RegularExpressions;
using DecembristChatBotSharp.DI;
using DecembristChatBotSharp.Entity.Configs;
using HtmlAgilityPack;
using JasperFx.Core;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

public record TelegramRandomMeme(string PhotoLink);

[Singleton]
public partial class TelegramPostService(
    IHttpClientFactory httpClientFactory,
    Random random,
    ChatConfigService chatConfigService)
{
    private const string TelegramChannelUrlFormat = "https://t.me/s/{0}";
    private const string TelegramPostUrlFormat = "https://t.me/{0}/{1}?embed=1&mode=tme";

    [GeneratedRegex(@"background-image:url\('(?<url>.*?)'\)")]
    private static partial Regex BackgroundImageRegex();

    public async Task<Option<TelegramRandomMeme>> GetRandomPostPicture(long chatId)
    {
        var maybeConfig = await chatConfigService.GetConfig(chatId, config => config.TelegramPostConfig);
        if (!maybeConfig.TryGetSome(out var telegramPostConfig))
        {
            return chatConfigService.LogNonExistConfig(None, nameof(TelegramPostConfig));
        }

        var maxGetPostRetries = telegramPostConfig.MaxGetPostRetries;
        var channelNames = telegramPostConfig.ChannelNames;
        var scanPostCount = telegramPostConfig.ScanPostCount;
        var randomChannel = channelNames[random.Next(0, channelNames.Length)];

        var httpClient = httpClientFactory.CreateClient(HttpClientConfiguration.TelegramMemeClient);

        var maybePostId = await GetLastPostId(randomChannel, httpClient)
            .ToTryAsync()
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to get last post id for channel {channel}, error: {error}", randomChannel,
                    ex.Message);
                return None;
            });

        if (!maybePostId.IsSome)
        {
            Log.Error("Failed to get last post id for channel {0}", randomChannel);
            return None;
        }

        for (var i = 0; i < maxGetPostRetries; i++)
        {
            var maybeMeme = await GetPostPicture(maybePostId, randomChannel, scanPostCount, httpClient);
            if (maybeMeme.IsSome) return maybeMeme;
        }

        return None;
    }

    private async Task<Option<TelegramRandomMeme>> GetPostPicture(Option<int> postId, string randomChannel,
        int scanPostCount, HttpClient httpClient)
    {
        var maybePostUrl =
            from id in postId
            let firstPostId = Math.Max(1, id - scanPostCount)
            let randomId = random.Next(firstPostId, id + 1)
            let postUrl = string.Format(TelegramPostUrlFormat, randomChannel, randomId)
            select postUrl;

        var maybeHtml =
            from postUrl in maybePostUrl.ToTryOptionAsync()
            from html in httpClient.GetStringAsync(postUrl).ToTryOption()
            select html;

        var maybeMeme =
            from html in await maybeHtml
            let wrapMode = GetWrapMode(html)
            where wrapMode != null
            let style = wrapMode.GetAttributeValue("style", "")
            let match = BackgroundImageRegex().Match(style)
            where match.Success
            let imageUrl = match.Groups["url"].Value
            where !string.IsNullOrEmpty(imageUrl)
            select new TelegramRandomMeme(imageUrl);

        return maybeMeme
            .ToTry()
            .IfFail(ex =>
            {
                Log.Error(ex, "Failed to get random post {0} picture for telegram channel {1}", postId, randomChannel);
                return None;
            });
    }

    private async Task<Option<int>> GetLastPostId(string channel, HttpClient httpClient)
    {
        var url = string.Format(TelegramChannelUrlFormat, channel);

        var html = await httpClient.GetStringAsync(url);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var messages = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tgme_widget_message')]");
        return
            from message in messages.LastOrNone()
            where message != null
            let node = message.SelectSingleNode(".//a[contains(@class, 'tgme_widget_message_date')]")
            where node != null
            let href = node.GetAttributeValue("href", "")
            where href.IsNotEmpty() && href.Contains($"/{channel}/")
            let parts = href.Split('/')
            where parts.Length > 0
            let id = int.Parse(parts.Last())
            where id > 0
            select id;
    }

    private HtmlNode GetWrapMode(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        return doc.DocumentNode.SelectSingleNode("//a[contains(@class, 'tgme_widget_message_photo_wrap')]");
    }
}
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class MemeDownloadService(
    IHttpClientFactory httpClientFactory,
    BotClient botClient,
    CancellationTokenSource cancelToken)
{
    public async Task<bool> SendMeme(
        long chatId,
        string imageUrl,
        string clientName,
        string caption,
        ParseMode? parseMode = null,
        string logMessage = "Sent meme")
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(clientName);
            using var response = await httpClient.GetAsync(imageUrl, cancelToken.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancelToken.Token);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancelToken.Token);
            memoryStream.Position = 0;

            var fileName = GetFileName(imageUrl);

            await botClient.SendPhoto(
                chatId,
                InputFile.FromStream(memoryStream, fileName),
                caption: caption,
                parseMode: parseMode ?? ParseMode.None,
                cancellationToken: cancelToken.Token);

            Log.Information("{LogMessage} to chat {chatId}", logMessage, chatId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send meme {imageUrl} to chat {chatId}", imageUrl, chatId);
            return false;
        }
    }

    private static string GetFileName(string url)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName) || !fileName.Contains('.'))
        {
            fileName = "meme.jpg";
        }

        return fileName;
    }
}
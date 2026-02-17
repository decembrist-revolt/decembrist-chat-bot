using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

public record DeepSeekRequest(
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("parent_message_id")]
    string ParentMessageId
);

public record DeepSeekResponse(
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("finish_reason")]
    string FinishReason,
    [property: JsonPropertyName("response_id")]
    string ResponseId
);

public record ModerationResponse(
    [property: JsonPropertyName("isScam")] bool IsScam
);

[Singleton]
public class DeepSeekService(
    IHttpClientFactory httpClientFactory,
    AppConfig appConfig)
{
    private readonly string _moderatorPrompt = appConfig.FilterJobConfig.DeepSeekPrompt +
                                               string.Join("\n - ", appConfig.FilterJobConfig.ScamTraitors) +
                                               $$"""
                                                 Сообщение для проверки:
                                                 "{0}"

                                                 Ответь СТРОГО в формате JSON, без дополнительного текста:
                                                 {"isScam": true} или {"isScam": false}
                                                 Отвечай только JSON, ничего больше.
                                                 """;

    public async Task<Option<string>> GetChatResponse(string userMessage, long chatId, long userId)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient("DeepSeekClient");

            var requestData = new DeepSeekRequest(
                Message: userMessage,
                ParentMessageId: ""
            );

            var jsonRequest = JsonSerializer.Serialize(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, appConfig.DeepSeekConfig.ApiUrl);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", appConfig.DeepSeekConfig.BearerToken);

            using var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("DeepSeek API error: {StatusCode}, {Content}", response.StatusCode, errorContent);
                return None;
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            var apiResponse = JsonSerializer.Deserialize<DeepSeekResponse>(responseContent);

            if (apiResponse == null || string.IsNullOrWhiteSpace(apiResponse.Message))
            {
                Log.Warning("DeepSeek API returned empty or invalid response");
                return None;
            }

            Log.Information("DeepSeek response for chat {ChatId}, user {UserId}: {Message} (ResponseId: {ResponseId})",
                chatId, userId, apiResponse.Message, apiResponse.ResponseId);

            return Some(apiResponse.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DeepSeek response for chat {ChatId}, user {UserId}", chatId, userId);
            return None;
        }
    }

    public async Task<Option<bool>> GetModerateVerdict(string messageText, long chatId, long userId,
        string parentMessageText = "")
    {
        var prompt = string.Format(_moderatorPrompt, messageText);
        try
        {
            using var httpClient = httpClientFactory.CreateClient("DeepSeekClient");

            var requestData = new DeepSeekRequest(
                Message: prompt,
                ParentMessageId: parentMessageText
            );
            var jsonRequest = JsonSerializer.Serialize(requestData);
            var request = new HttpRequestMessage(HttpMethod.Post, appConfig.DeepSeekConfig.ApiUrl);
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", appConfig.DeepSeekConfig.BearerToken);

            using var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error("DeepSeek moderation API error: {StatusCode}, {Content}", response.StatusCode, errorContent);
                return None;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            ModerationResponse? moderation = null;
            try
            {
                moderation = JsonSerializer.Deserialize<ModerationResponse>(responseContent);
                if (moderation == null)
                {
                    var start = responseContent.IndexOf('{');
                    var end = responseContent.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        var json = responseContent.Substring(start, end - start + 1);
                        moderation = JsonSerializer.Deserialize<ModerationResponse>(json);
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Failed to parse moderation JSON response for chat {ChatId}: {Response}", chatId,
                    responseContent);
                return None;
            }

            if (moderation == null)
            {
                Log.Warning("Failed to deserialize moderation response for chat {ChatId}: {Response}", chatId,
                    responseContent);
                return None;
            }

            Log.Information("DeepSeek moderation verdict for chat {ChatId}, user {UserId}: isScam={IsScam}", chatId,
                userId, moderation.IsScam);
            return Some(moderation.IsScam);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get DeepSeek moderation verdict for chat {ChatId}, user {UserId}", chatId, userId);
            return None;
        }
    }
}
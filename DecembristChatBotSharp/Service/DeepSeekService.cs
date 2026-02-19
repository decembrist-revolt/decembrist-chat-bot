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

    public async Task<Option<bool>> GetModerateVerdict(string prompt, long chatId, long userId)
    {
        var maybeResponse = await GetChatResponse(prompt, chatId, userId);
        if (!maybeResponse.TryGetSome(out var message)) return None;

        try
        {
            var moderation = JsonSerializer.Deserialize<ModerationResponse>(message);

            if (moderation == null)
            {
                Log.Warning("Failed to deserialize moderation response for chat {ChatId}: {Response}", chatId,
                    message);
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
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class KeycloakService(
    AppConfig appConfig,
    IHttpClientFactory httpClientFactory,
    CancellationTokenSource cancelToken
)
{
    public const string TokenGrantType = "client_credentials";
    public const string TelegramIdAttribute = "telegram-id";
    public const string ScopeAttribute = "scope";
    public const string ScopeValue = "user-product";
    public const string ClientIdAttribute = "client_id";
    public const string ClientSecretAttribute = "client_secret";
    public const string GrantTypeAttribute = "grant_type";

    public async Task<Option<string>> GetClientToken()
    {
        var (serverUrl, realm, clientId, clientSecret) = GetConfig();

        var tokenEndpoint = $"{serverUrl}/realms/{realm}/protocol/openid-connect/token";
        var requestBody = new Dictionary<string, string>
        {
            { ClientIdAttribute, clientId },
            { ClientSecretAttribute, clientSecret },
            { GrantTypeAttribute, TokenGrantType },
            { ScopeAttribute, ScopeValue }
        };

        using var client = httpClientFactory.CreateClient();
        var requestContent = new FormUrlEncodedContent(requestBody);

        var tryGetTokenContent = client.PostAsync(tokenEndpoint, requestContent, cancelToken.Token)
            .ToTryAsync()
            .Ensure(
                response => response.IsSuccessStatusCode,
                response => $"Status: {response.StatusCode}, Message: {response.ReasonPhrase}")
            .MapAsync(response => response.Content.ReadAsStringAsync())
            .Ensure(content => content is not null, "Empty response body");

        return await tryGetTokenContent
            .Map(content => JsonSerializer.Deserialize<KeycloakToken>(content))
            .Ensure(contentMap => contentMap is not null, "Empty keycloak token body")
            .Map(contentMap => contentMap!.AccessToken)
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to retrieve Keycloak token");
                return None;
            });
    }

    public async Task<Option<KeycloakUser>> GetUserById(string token, string userId)
    {
        var (serverUrl, realm, _, _) = GetConfig();
        var userEndpoint = $"{serverUrl}/admin/realms/{realm}/users/{Uri.EscapeDataString(userId)}";

        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tryGet = client.GetAsync(userEndpoint, cancelToken.Token)
            .ToTryAsync()
            .Ensure(
                resp => resp.IsSuccessStatusCode,
                resp => $"Status: {resp.StatusCode}, Message: {resp.ReasonPhrase}"
            )
            .MapAsync(resp => resp.Content.ReadAsStringAsync())
            .Ensure(json => !string.IsNullOrWhiteSpace(json), "Empty response body");

        return await tryGet
            .Map(json => JsonSerializer.Deserialize<KeycloakUser>(json))
            .Ensure(user => user is not null, "Failed to deserialize KeycloakUser")
            .Map(user => user!)
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to retrieve Keycloak user");
                return None;
            });
    }

    public Option<long> GetTelegramId(KeycloakUser user)
    {
        if (user.Attributes is null) return None;
        if (!user.Attributes.TryGetValue(TelegramIdAttribute, out var telegramIdList)) return None;
        if (telegramIdList.Count == 0)
        {
            Log.Error("No Telegram ID found for user {UserId}", user.Id);
            return None;
        }

        if (long.TryParse(telegramIdList[0], out var telegramId)) return telegramId;

        Log.Warning("Invalid Telegram ID format for user {UserId}: {TelegramId}", user.Id, telegramIdList[0]);
        return None;
    }

    private KeycloakConfig GetConfig() =>
        appConfig.KeycloakConfig ?? throw new Exception("Keycloak configuration is not provided");
}

public record KeycloakToken(
    [property: JsonPropertyName("access_token")]
    string AccessToken);

public record KeycloakUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")]
    string Username,
    [property: JsonPropertyName("firstName")]
    string? FirstName,
    [property: JsonPropertyName("lastName")]
    string? LastName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("emailVerified")]
    bool? EmailVerified,
    [property: JsonPropertyName("attributes")]
    IDictionary<string, List<string>>? Attributes,
    [property: JsonPropertyName("userProfileMetadata")]
    IDictionary<string, object>? UserProfileMetadata,
    [property: JsonPropertyName("self")] string? Self,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("createdTimestamp")]
    long? CreatedTimestamp,
    [property: JsonPropertyName("enabled")]
    bool? Enabled,
    [property: JsonPropertyName("totp")] bool? Totp,
    [property: JsonPropertyName("federationLink")]
    string? FederationLink,
    [property: JsonPropertyName("serviceAccountClientId")]
    string? ServiceAccountClientId,
    [property: JsonPropertyName("credentials")]
    List<CredentialRepresentation>? Credentials,
    [property: JsonPropertyName("disableableCredentialTypes")]
    System.Collections.Generic.HashSet<string>? DisableableCredentialTypes,
    [property: JsonPropertyName("requiredActions")]
    List<string>? RequiredActions,
    [property: JsonPropertyName("federatedIdentities")]
    List<FederatedIdentityRepresentation>? FederatedIdentities,
    [property: JsonPropertyName("realmRoles")]
    List<string>? RealmRoles,
    [property: JsonPropertyName("clientRoles")]
    IDictionary<string, List<string>>? ClientRoles,
    [property: JsonPropertyName("clientConsents")]
    List<UserConsentRepresentation>? ClientConsents,
    [property: JsonPropertyName("notBefore")]
    int? NotBefore,
    [property: JsonPropertyName("applicationRoles")]
    IDictionary<string, List<string>>? ApplicationRoles,
    [property: JsonPropertyName("socialLinks")]
    List<SocialLinkRepresentation>? SocialLinks,
    [property: JsonPropertyName("groups")] List<string>? Groups,
    [property: JsonPropertyName("access")] IDictionary<string, bool>? Access
);

public record CredentialRepresentation(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("userLabel")]
    string? UserLabel,
    [property: JsonPropertyName("createdDate")]
    long? CreatedDate,
    [property: JsonPropertyName("secretData")]
    string? SecretData,
    [property: JsonPropertyName("credentialData")]
    string? CredentialData,
    [property: JsonPropertyName("priority")]
    int? Priority,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("temporary")]
    bool? Temporary,
    // deprecated fields
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("hashedSaltedValue")]
    string? HashedSaltedValue,
    [property: JsonPropertyName("salt")] string? Salt,
    [property: JsonPropertyName("hashIterations")]
    int? HashIterations,
    [property: JsonPropertyName("counter")]
    int? Counter,
    [property: JsonPropertyName("algorithm")]
    string? Algorithm,
    [property: JsonPropertyName("digits")] int? Digits,
    [property: JsonPropertyName("period")] int? Period,
    [property: JsonPropertyName("config")] Dictionary<string, List<string>>? Config,
    [property: JsonPropertyName("federationLink")]
    string? FederationLink
);

public record FederatedIdentityRepresentation(
    [property: JsonPropertyName("identityProvider")]
    string? IdentityProvider,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("userName")]
    string? UserName
);

public record UserConsentRepresentation(
    [property: JsonPropertyName("clientId")]
    string? ClientId,
    [property: JsonPropertyName("grantedClientScopes")]
    List<string>? GrantedClientScopes,
    [property: JsonPropertyName("createdDate")]
    long? CreatedDate,
    [property: JsonPropertyName("lastUpdatedDate")]
    long? LastUpdatedDate,
    [property: JsonPropertyName("grantedRealmRoles")]
    List<string>? GrantedRealmRoles
);

public record SocialLinkRepresentation(
    [property: JsonPropertyName("socialProvider")]
    string? SocialProvider,
    [property: JsonPropertyName("socialUserId")]
    string? SocialUserId,
    [property: JsonPropertyName("socialUsername")]
    string? SocialUsername
);
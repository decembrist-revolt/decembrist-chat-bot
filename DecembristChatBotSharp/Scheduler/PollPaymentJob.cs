using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using DecembristChatBotSharp.Telegram;
using JasperFx.Core;
using LanguageExt.Common;
using MongoDB.Bson;
using Quartz;
using Serilog;
using static DecembristChatBotSharp.Entity.PremiumMemberOperationType;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace DecembristChatBotSharp.Scheduler;

public class PollPaymentJob(
    AppConfig appConfig,
    MessageAssistance messageAssistance,
    PremiumMemberRepository premiumMemberRepository,
    HistoryLogRepository historyLogRepository,
    UserProductRepository userProductRepository,
    KeycloakService keycloakService,
    IHttpClientFactory httpClientFactory,
    PollPaymentOffsetRepository pollPaymentOffsetRepository,
    MongoDatabase db,
    CancellationTokenSource cancelToken
) : IRegisterJob
{
    private const string UserProductUri = "/api/user-product";
    private Option<string> _maybeToken;

    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task Register(IScheduler scheduler)
    {
        if (appConfig.PollPaymentConfig == null) return;
        
        var triggerKey = new TriggerKey(nameof(PollPaymentJob));

        var job = JobBuilder.Create<PollPaymentJob>()
            .WithIdentity(nameof(PollPaymentJob))
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInSeconds(appConfig.PollPaymentConfig.PollIntervalSeconds)
                .RepeatForever()
                .WithMisfireHandlingInstructionNextWithExistingCount())
            .Build();
        
        var existingTrigger = await scheduler.GetTrigger(triggerKey);

        if (existingTrigger != null)
        {
            await scheduler.RescheduleJob(triggerKey, trigger);
        }
        else
        {
            await scheduler.ScheduleJob(job, trigger);
        }
    }

    public async Task Execute(IJobExecutionContext context)
    {
        await PollPayment();
        // context.Scheduler...
    }

    private async Task PollPayment()
    {
        var pollConfig = appConfig.PollPaymentConfig;
        if (pollConfig == null) return;

        Log.Information("Executing PollPaymentJob");
        if (_maybeToken.IsNone) _maybeToken = await keycloakService.GetClientToken();
        if (_maybeToken.IsNone)
        {
            Log.Error("Failed to retrieve Keycloak token for PollPaymentJob");
            return;
        }

        var session = await db.OpenSession();
        session.StartTransaction();

        var maybeOffset = await GetOffset(session);
        if (maybeOffset.IsNone)
        {
            Log.Error("Failed to retrieve PollPaymentOffset in PollPaymentJob");
            await session.TryAbort(cancelToken.Token);

            return;
        }

        var offset = maybeOffset.IfNoneThrow();
        var token = _maybeToken.IfNoneThrow();

        await PollPayment(token, offset, pollConfig.ProductList.ToArr(), session);
        Log.Information("Execution PollPaymentJob done");
    }

    private async Task PollPayment(
        string token, long offset, Arr<ProductListItem> productList, IMongoSession session)
    {
        var fetchResult = await FetchUserProduct(token, offset, productList.Map(item => item.Regex));
        if (fetchResult.IsLeft)
        {
            var ex = fetchResult.IfRightThrow();
            Log.Error(ex, "Failed to fetch user products in PollPaymentJob");
            await session.TryAbort(cancelToken.Token);

            return;
        }

        var maybeUserProduct = fetchResult.IfLeftThrow();
        if (maybeUserProduct.IsNone)
        {
            Log.Information("No user products found for offset {0} in PollPaymentJob", offset);
            await session.TryAbort(cancelToken.Token);

            return;
        }

        var userProduct = maybeUserProduct.IfNoneThrow();
        if (!await CheckExistent(userProduct, session))
        {
            Log.Warning("User product {0} already exists, skipping in PollPaymentJob", userProduct.Id);
            await UpdateOffset(offset, session);

            return;
        }

        var product = userProduct.Product;
        var productListItem =
            productList.FirstOrDefault(item => Regex.IsMatch(product.Name, item.Regex, RegexOptions.IgnoreCase));
        if (productListItem?.Type is not { } type)
        {
            Log.Information("Product {0} not configured for PollPaymentJob, skipping", product.Name);
            await UpdateOffset(offset, session);

            return;
        }

        if (await HandleProduct(token, userProduct.UserId, userProduct.Id, type, product.MetaInfo, session))
        {
            await userProductRepository.AddUserProduct(userProduct, session);
            Log.Information("Handled product {0} for user {1} in PollPaymentJob", product.Name, userProduct.UserId);
            await UpdateOffset(offset, session);

            return;
        }

        await session.TryAbort(cancelToken.Token);
    }

    private async Task<Unit> UpdateOffset(long oldOffset, IMongoSession session)
    {
        var newOffset = oldOffset + 1;
        var updateResult = await pollPaymentOffsetRepository.Set(newOffset, session);
        if (updateResult.IsLeft)
        {
            var ex = updateResult.IfRightThrow();
            Log.Error(ex, "Failed to update PollPaymentOffset in PollPaymentJob");
            await session.TryAbort(cancelToken.Token);

            return unit;
        }

        Log.Information("Updated PollPaymentOffset from {0} to {1} in PollPaymentJob", oldOffset, newOffset);
        if (await session.TryCommit(cancelToken.Token))
        {
            Log.Information("Successfully committed PollPaymentJob transaction");
        }
        else
        {
            Log.Error("Failed to commit PollPaymentJob transaction");
            await session.TryAbort(cancelToken.Token);
        }

        return unit;
    }

    private async Task<bool> HandleProduct(
        string token, string userId, string userProductId, ProductType type, BsonDocument metaInfo, IMongoSession session)
    {
        var maybeUser = await keycloakService.GetUserById(token, userId);
        if (maybeUser.IsNone)
        {
            Log.Error("Failed to retrieve Keycloak user for product userId {0} in PollPaymentJob", userId);
            return false;
        }

        var maybeTelegramId = keycloakService.GetTelegramId(maybeUser.IfNoneThrow());
        if (maybeTelegramId.IsNone)
        {
            Log.Error("Failed to retrieve Telegram ID for user {0} in PollPaymentJob", userId);
            return false;
        }

        var telegramId = maybeTelegramId.IfNoneThrow();
        return type switch
        {
            ProductType.ChatPremium => await HandlePremium(telegramId, userProductId, metaInfo, session),
            _ => throw new NotSupportedException($"Unsupported product type: {type} in PollPaymentJob")
        };
    }

    private async Task<bool> HandlePremium(
        long telegramId, string userProductId, BsonDocument metaInfo, IMongoSession session)
    {
        var maybeInfo = DeserializeMetaInfo<ChatPremiumMetaInfo>(metaInfo);
        if (maybeInfo.IsLeft)
        {
            var ex = maybeInfo.IfRightThrow();
            Log.Error(ex, "Failed to deserialize ChatPremiumMetaInfo in PollPaymentJob");

            return false;
        }

        var (chatId, duration) = maybeInfo.IfLeftThrow();

        var result = await AddPremiumMember(chatId, telegramId, DateTime.UtcNow.Add(duration), session, userProductId);

        if (result == AddPremiumMemberResult.Error)
        {
            Log.Error("Failed to add premium member for {0} in chat {1}, duration {2} in PollPaymentJob",
                telegramId, chatId, duration);
            return false;
        }

        if (result == AddPremiumMemberResult.Add)
        {
            messageAssistance.SendAddPremiumMessage(chatId, telegramId, (int)duration.TotalDays);
        }
        else if (result == AddPremiumMemberResult.Update)
        {
            messageAssistance.SendUpdatePremiumMessage(chatId, telegramId, (int)duration.TotalDays);
        }

        Log.Information("{0} premium member for {1}, duration {2} in PollPaymentJob", result, telegramId, duration);
        return true;
    }

    private async Task<bool> CheckExistent(UserProduct content, IMongoSession session)
    {
        var maybeProducts = await userProductRepository.ExistsById(content.Id, session);
        if (maybeProducts.IsLeft)
        {
            var ex = maybeProducts.IfRightThrow();
            Log.Error(ex, "Failed to get existing user products in PollPaymentJob");
            await session.TryAbort(cancelToken.Token);

            return false;
        }

        var exists = maybeProducts.IfLeftThrow();
        return !exists;
    }

    private async Task<Option<long>> GetOffset(IMongoSession session)
    {
        var getResult = await pollPaymentOffsetRepository.Get(session);
        if (getResult.IsLeft)
        {
            var ex = getResult.IfRightThrow();
            Log.Error(ex, "Failed to get PollPaymentOffset in PollPaymentJob");

            return None;
        }

        var maybeOffset = getResult.IfLeftThrow();
        return maybeOffset.IsSome ? maybeOffset.IfNoneThrow().Offset : 0;
    }

    private async Task<Either<Error, Option<UserProduct>>> FetchUserProduct(
        string token, long offset, Arr<string> productRegexes)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pollConfig = appConfig.PollPaymentConfig;
        if (pollConfig == null)
        {
            throw new InvalidOperationException("PollPaymentConfig is not set for FetchUserProducts");
        }

        if (productRegexes.Count == 0)
        {
            Log.Error("No product names configured for PollPaymentJob");
            return Error.New("No product names configured for PollPaymentJob");
        }

        var sb = new StringBuilder();
        foreach (var regex in productRegexes)
        {
            sb.Append("productRegexes=").Append(Uri.EscapeDataString(regex)).Append('&');
        }

        sb.Append("offset=").Append(offset);

        var url = $"{pollConfig.ServiceUrl.TrimEnd('/')}{UserProductUri}?{sb}";

        var responseEither = await client.GetAsync(url).ToTryAsync().ToEither();
        if (responseEither.IsLeft)
        {
            var ex = responseEither.IfRightThrow();
            Log.Error(ex, "Failed to fetch user products from {0} in PollPaymentJob", url);
            return ex;
        }

        var response = responseEither.IfLeftThrow();
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.NotFound:
                Log.Information("No user products found at {0} in PollPaymentJob", url);
                return Option<UserProduct>.None;
            case HttpStatusCode.Unauthorized:
                Log.Error("Unauthorized access while fetching user products from {0} in PollPaymentJob", url);
                _maybeToken = await keycloakService.GetClientToken();
                return Error.New("Unauthorized access while fetching user products");
            case HttpStatusCode.InternalServerError:
                Log.Warning("Internal server error while fetching user products from {0} in PollPaymentJob", url);
                return Error.New("Internal server error while fetching user products");
            default:
                Log.Error("Unexpected error while fetching user products from {0}, status code: {1} in PollPaymentJob",
                    url, response.StatusCode);
                return Error.New("Unexpected error while fetching user products");
        }

        return await response.Content.ReadAsStringAsync().ToTryAsync()
            .Ensure(content => content.IsNotEmpty(), "Empty response body")
            .Map(json => JsonSerializer.Deserialize<UserProduct>(json, _options))
            .Ensure(product => product is not null, "Failed to deserialize UserProductPagedResponse")
            .Match(Optional, ex =>
            {
                Log.Error(ex, "Failed to retrieve Keycloak user");
                return None;
            });
    }

    private Either<Exception, T> DeserializeMetaInfo<T>(BsonDocument metaInfo) =>
        Try(() => JsonSerializer.Deserialize<T>(metaInfo.ToJson(), _options))
            .Map(value => value ?? throw new Exception("Failed to deserialize MetaInfo is null"))
            .ToEither();

    private async Task<AddPremiumMemberResult> AddPremiumMember(
        long chatId,
        long telegramId,
        DateTime expirationDate,
        IMongoSession session,
        string userProductId)
    {
        const int level = 1;
        CompositeId id = (telegramId, chatId);
        var getResult = await premiumMemberRepository.GetById(id, session);
        if (getResult.IsLeft)
        {
            var ex = getResult.IfRightThrow();
            Log.Error(ex, "Failed to get premium member {0} in chat {1}", telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        var maybeMember = getResult.IfLeftThrow();
        var member = maybeMember.Map(member => member with
        {
            ExpirationDate = expirationDate + (member.ExpirationDate - DateTime.UtcNow)
        }).IfNone(new PremiumMember((telegramId, chatId), expirationDate, level));
        var addResult = await premiumMemberRepository.AddPremiumMember(member, session);

        if (addResult == AddPremiumMemberResult.Error)
        {
            Log.Warning("Failed to add premium member {0} to chat {1}", telegramId, chatId);
            return AddPremiumMemberResult.Error;
        }

        await historyLogRepository.LogPremium(
            chatId, telegramId, Payment, expirationDate, level, session, userProductId: userProductId);

        Log.Information(
            "{0} premium member {1} to chat {2} exp: {3}", addResult, telegramId, chatId, expirationDate);
        return addResult;
    }
}
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BreakMeIfYouCan.Api.Arena;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BreakMeIfYouCan.Tests.Integration.Infrastructure;

public static class TestJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}

/// <summary>A browser-like visitor: one persistent cookie jar per client.</summary>
public sealed class ArenaClient : IDisposable
{
    private readonly HttpClient _client;

    public ArenaClient(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost"),
        });
    }

    public HttpClient Http => _client;

    public async Task<SandboxView> GetSessionAsync()
    {
        var response = await _client.GetAsync(new Uri("/api/arena/session", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SandboxView>(TestJson.Options))!;
    }

    public async Task<SandboxView> ResetAsync()
    {
        var response = await _client.PostAsync(new Uri("/api/arena/reset", UriKind.Relative), content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SandboxView>(TestJson.Options))!;
    }

    public Task<HttpResponseMessage> TransferAsync(
        Guid from,
        Guid to,
        long amountCents,
        string strategy = "optimistic",
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/arena/transfers", UriKind.Relative))
        {
            Content = JsonContent.Create(new TransferRequest
            {
                FromAccountId = from,
                ToAccountId = to,
                AmountCents = amountCents,
                Strategy = strategy,
            }),
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return _client.SendAsync(request, cancellationToken);
    }

    public async Task<ArenaRunResult> RunAsync(ArenaRunRequest request)
    {
        var response = await _client.PostAsJsonAsync(new Uri("/api/arena/runs", UriKind.Relative), request, TestJson.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ArenaRunResult>(TestJson.Options))!;
    }

    public async Task<LedgerView> GetLedgerAsync(int limit = 100)
    {
        var response = await _client.GetAsync(new Uri("/api/arena/ledger?limit=" + limit, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LedgerView>(TestJson.Options))!;
    }

    public static async Task<TransferResponse?> ReadTransferAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<TransferResponse>(TestJson.Options);

    public void Dispose() => _client.Dispose();
}

using System.Globalization;
using System.Text.Json;
using BreakMeIfYouCan.Evidence.Verification;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Proof;

public interface IRuntimeImageProbe
{
    Task<RuntimeObservation> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Asks Fly.io's Machines API which image the running machine actually uses. A deployment
/// environment variable is deliberately never used here: an operator could set it to
/// match the attestation, which would make the comparison meaningless.
/// </summary>
public sealed class FlyRuntimeProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<ProofOptions> options,
    ILogger<FlyRuntimeProbe> logger) : IRuntimeImageProbe
{
    public const string HttpClientName = "fly";

    private readonly FlyProofOptions _fly = options.Value.Fly;

    public async Task<RuntimeObservation> ProbeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_fly.AppName))
        {
            return Unavailable("Proof:Fly:AppName is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_fly.ApiToken))
        {
            return Unavailable("A Fly.io API token is not configured, so the running image cannot be observed.");
        }

        var url = _fly.ApiBaseUrl.TrimEnd('/') + "/v1/apps/" + _fly.AppName + "/machines";

        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _fly.ApiToken);

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable("The Fly Machines API returned " + (int)response.StatusCode + ".", url);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Unavailable("The Fly Machines API returned an unexpected response shape.", url);
            }

            var machine = SelectMachine(document.RootElement);
            if (machine is null)
            {
                return Unavailable("The app has no machine to inspect.", url);
            }

            var selected = machine.Value;
            var state = ReadString(selected, "state") ?? "unknown";
            var image = ReadImage(selected);

            return new RuntimeObservation
            {
                Available = true,
                Digest = DigestReference.Extract(image),
                ImageReference = image,
                AppName = _fly.AppName,
                MachineId = ReadString(selected, "id"),
                Region = ReadString(selected, "region"),
                Healthy = state switch
                {
                    "started" => true,
                    "stopped" or "failed" or "destroyed" or "suspended" => false,
                    _ => null,
                },
                HealthDetail = "Machine state: " + state + ".",
                MachineCreatedAt = ReadTimestamp(selected, "created_at"),
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "fly-machines-api",
                SourceUrl = url,
                Failure = DigestReference.Extract(image) is null
                    ? "The platform reports the image as a tag, not a digest, so it cannot be compared."
                    : null,
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "The Fly Machines API could not be reached.");
            return Unavailable("The Fly Machines API could not be reached: " + exception.Message, url);
        }
    }

    private JsonElement? SelectMachine(JsonElement machines)
    {
        JsonElement? first = null;

        foreach (var machine in machines.EnumerateArray())
        {
            first ??= machine;

            if (!string.IsNullOrWhiteSpace(_fly.MachineId))
            {
                if (string.Equals(ReadString(machine, "id"), _fly.MachineId, StringComparison.Ordinal))
                {
                    return machine;
                }

                continue;
            }

            if (string.Equals(ReadString(machine, "state"), "started", StringComparison.OrdinalIgnoreCase))
            {
                return machine;
            }
        }

        return first;
    }

    private static string? ReadImage(JsonElement machine)
    {
        if (!machine.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(config, "image");
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
    {
        var raw = ReadString(element, name);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static RuntimeObservation Unavailable(string failure, string? sourceUrl = null) => new()
    {
        Available = false,
        Failure = failure,
        ObservedAt = DateTimeOffset.UtcNow,
        Source = "fly-machines-api",
        SourceUrl = sourceUrl,
    };
}

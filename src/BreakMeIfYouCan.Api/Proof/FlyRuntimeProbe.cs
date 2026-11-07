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
/// Asks Fly.io's Machines API which image the running machines actually use.
///
/// The observed digest comes exclusively from the top-level "image_ref.digest" that Fly
/// reports for a Machine. "config.image" is the image that was requested and is never used
/// as evidence of what is running.
///
/// A deployment environment variable is deliberately not used either: an operator could set
/// it to match the attestation, which would make the comparison meaningless.
/// </summary>
public sealed class FlyRuntimeProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<ProofOptions> options,
    ILogger<FlyRuntimeProbe> logger) : IRuntimeImageProbe
{
    public const string HttpClientName = "fly";

    private const int RefetchAttempts = 2;
    private static readonly TimeSpan RefetchDelay = TimeSpan.FromMilliseconds(200);

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

        var baseUrl = _fly.ApiBaseUrl.TrimEnd('/');
        var machinesUrl = baseUrl + "/v1/apps/" + Uri.EscapeDataString(_fly.AppName) + "/machines";

        try
        {
            using var client = CreateClient();

            using var response = await client.GetAsync(machinesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unavailable("The Fly Machines API returned " + (int)response.StatusCode + ".", machinesUrl);
            }

            var machines = await ReadMachineListAsync(response, cancellationToken);
            if (machines is null)
            {
                return Unavailable("The Fly Machines API returned an unexpected response shape.", machinesUrl);
            }

            var selected = SelectMachines(machines);
            if (selected.Error is not null)
            {
                return Unavailable(selected.Error, machinesUrl);
            }

            // A digest can be missing from the list representation. Ask the single-machine
            // endpoint (documented as GET /v1/apps/{app}/machines/{id}) before giving up.
            var resolved = new List<MachineSnapshot>(selected.Machines.Count);
            foreach (var machine in selected.Machines)
            {
                var current = machine;

                if (current.Digest is null && !string.IsNullOrWhiteSpace(current.Id))
                {
                    var refreshed = await RefetchMachineAsync(client, baseUrl, current.Id!, cancellationToken);
                    if (refreshed is not null)
                    {
                        current = refreshed;
                    }
                }

                resolved.Add(current);
            }

            return BuildObservation(resolved, machinesUrl);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "The Fly Machines API could not be reached.");
            return Unavailable("The Fly Machines API could not be reached: " + exception.Message, machinesUrl);
        }
    }

    private MachineSelection SelectMachines(IReadOnlyList<MachineSnapshot> machines)
    {
        if (!string.IsNullOrWhiteSpace(_fly.MachineId))
        {
            var configured = machines.FirstOrDefault(machine =>
                string.Equals(machine.Id, _fly.MachineId, StringComparison.Ordinal));

            return configured is null
                ? MachineSelection.Failed(
                    "The configured machine '" + _fly.MachineId + "' was not found in app '" + _fly.AppName +
                    "'. The probe will not silently select a different machine.")
                : MachineSelection.Success([configured]);
        }

        var started = machines
            .Where(machine => string.Equals(machine.State, "started", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return started.Count == 0
            ? MachineSelection.Failed(
                "No machine is in the 'started' state, so there is no running image to observe.")
            : MachineSelection.Success(started);
    }

    private static RuntimeObservation BuildObservation(IReadOnlyList<MachineSnapshot> machines, string sourceUrl)
    {
        var states = string.Join(", ", machines.Select(machine => machine.State));
        var health = ComputeHealth(machines);
        var healthDetail = "Observed " + machines.Count + " machine(s); states: " + states + ".";
        var imageReference = machines
            .Select(machine => machine.ImageReference)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var unresolved = machines.Where(machine => machine.Digest is null).ToList();
        if (unresolved.Count > 0)
        {
            return new RuntimeObservation
            {
                Available = true,
                Digest = null,
                ImageReference = imageReference,
                Registry = FirstRegistry(machines),
                Repository = FirstRepository(machines),
                MachineCount = machines.Count,
                MachineId = machines.Count == 1 ? machines[0].Id : null,
                Region = SingleRegion(machines),
                Healthy = health,
                HealthDetail = healthDetail,
                MachineCreatedAt = EarliestCreated(machines),
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "fly-machines-api",
                SourceUrl = sourceUrl,
                Failure =
                    unresolved.Count + " of " + machines.Count +
                    " observed machine(s) did not report image_ref.digest. The platform's resolved digest is the only accepted evidence.",
            };
        }

        var digests = machines
            .Select(machine => machine.Digest!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (digests.Count > 1)
        {
            return new RuntimeObservation
            {
                Available = true,
                Digest = null,
                ImageReference = imageReference,
                Registry = FirstRegistry(machines),
                Repository = FirstRepository(machines),
                MachineCount = machines.Count,
                MachineId = null,
                Region = SingleRegion(machines),
                Healthy = health,
                HealthDetail = healthDetail,
                MachineCreatedAt = EarliestCreated(machines),
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "fly-machines-api",
                SourceUrl = sourceUrl,
                Failure =
                    "The " + machines.Count + " running machines report different image digests (" +
                    string.Join(", ", digests) + "). A single running image cannot be verified.",
            };
        }

        var repositories = machines
            .Select(machine => (Registry: machine.Image?.Registry, Repository: machine.Image?.Repository))
            .Distinct()
            .ToList();

        if (repositories.Count > 1)
        {
            return new RuntimeObservation
            {
                Available = true,
                Digest = null,
                ImageReference = imageReference,
                Registry = FirstRegistry(machines),
                Repository = FirstRepository(machines),
                MachineCount = machines.Count,
                MachineId = null,
                Region = SingleRegion(machines),
                Healthy = health,
                HealthDetail = healthDetail,
                MachineCreatedAt = EarliestCreated(machines),
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "fly-machines-api",
                SourceUrl = sourceUrl,
                Failure = "The running machines report different image repositories, so the observed image is ambiguous.",
            };
        }

        return new RuntimeObservation
        {
            Available = true,
            Digest = digests[0],
            ImageReference = imageReference,
            Registry = FirstRegistry(machines),
            Repository = FirstRepository(machines),
            MachineCount = machines.Count,
            MachineId = machines.Count == 1 ? machines[0].Id : null,
            Region = SingleRegion(machines),
            Healthy = health,
            HealthDetail = healthDetail,
            MachineCreatedAt = EarliestCreated(machines),
            ObservedAt = DateTimeOffset.UtcNow,
            Source = "fly-machines-api",
            SourceUrl = sourceUrl,
            Failure = null,
        };
    }

    private async Task<MachineSnapshot?> RefetchMachineAsync(
        HttpClient client,
        string baseUrl,
        string machineId,
        CancellationToken cancellationToken)
    {
        var url = baseUrl + "/v1/apps/" + Uri.EscapeDataString(_fly.AppName) + "/machines/" + Uri.EscapeDataString(machineId);

        MachineSnapshot? lastSnapshot = null;

        for (var attempt = 1; attempt <= RefetchAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var snapshot = MachineSnapshot.From(document.RootElement);
                        logger.LogInformation(
                            "Re-fetched machine {MachineId} from the single-machine endpoint (attempt {Attempt}).",
                            machineId,
                            attempt);

                        // The endpoint can return an empty image_ref while a machine is being
                        // replaced. Keep the last snapshot but retry until the digest appears.
                        if (snapshot.Digest is not null)
                        {
                            return snapshot;
                        }

                        lastSnapshot = snapshot;
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogDebug(exception, "The single-machine re-fetch failed for {MachineId}.", machineId);
            }

            if (attempt < RefetchAttempts)
            {
                await Task.Delay(RefetchDelay, cancellationToken);
            }
        }

        return lastSnapshot;
    }

    private static async Task<List<MachineSnapshot>?> ReadMachineListAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return document.RootElement.EnumerateArray().Select(MachineSnapshot.From).ToList();
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _fly.ApiToken);
        return client;
    }

    private static bool? ComputeHealth(IReadOnlyList<MachineSnapshot> machines)
    {
        if (machines.Any(machine => machine.State is "stopped" or "failed" or "destroyed" or "suspended"))
        {
            return false;
        }

        return machines.All(machine => string.Equals(machine.State, "started", StringComparison.OrdinalIgnoreCase))
            ? true
            : null;
    }

    private static string? FirstRegistry(IReadOnlyList<MachineSnapshot> machines) =>
        machines.Select(machine => machine.Image?.Registry).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? FirstRepository(IReadOnlyList<MachineSnapshot> machines) =>
        machines.Select(machine => machine.Image?.Repository).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? SingleRegion(IReadOnlyList<MachineSnapshot> machines)
    {
        var regions = machines
            .Select(machine => machine.Region)
            .Where(region => !string.IsNullOrWhiteSpace(region))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return regions.Count == 1 ? regions[0] : null;
    }

    private static DateTimeOffset? EarliestCreated(IReadOnlyList<MachineSnapshot> machines) =>
        machines
            .Select(machine => machine.CreatedAt)
            .Where(created => created is not null)
            .Min();

    private static RuntimeObservation Unavailable(string failure, string? sourceUrl = null) => new()
    {
        Available = false,
        Failure = failure,
        // Nothing was observed, so nothing is timestamped as observed.
        ObservedAt = null,
        Source = "fly-machines-api",
        SourceUrl = sourceUrl,
    };

    private sealed record MachineSelection(IReadOnlyList<MachineSnapshot> Machines, string? Error)
    {
        public static MachineSelection Success(IReadOnlyList<MachineSnapshot> machines) => new(machines, null);

        public static MachineSelection Failed(string error) => new([], error);
    }

    private sealed record MachineImage(string? Registry, string? Repository, string? Tag, string? Digest)
    {
        public string? Reference
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Registry) || string.IsNullOrWhiteSpace(Repository))
                {
                    return null;
                }

                var reference = Registry + "/" + Repository;
                if (!string.IsNullOrWhiteSpace(Tag))
                {
                    reference += ":" + Tag;
                }

                if (!string.IsNullOrWhiteSpace(Digest))
                {
                    reference += "@" + Digest;
                }

                return reference;
            }
        }
    }

    private sealed record MachineSnapshot(
        string? Id,
        string State,
        string? Region,
        DateTimeOffset? CreatedAt,
        MachineImage? Image)
    {
        /// <summary>Normalised sha256 digest from image_ref only. Never from config.image.</summary>
        public string? Digest => DigestReference.Extract(Image?.Digest);

        public string? ImageReference => Image?.Reference;

        public static MachineSnapshot From(JsonElement element)
        {
            MachineImage? image = null;

            if (element.TryGetProperty("image_ref", out var imageRef) && imageRef.ValueKind == JsonValueKind.Object)
            {
                image = new MachineImage(
                    ReadString(imageRef, "registry"),
                    ReadString(imageRef, "repository"),
                    ReadString(imageRef, "tag"),
                    ReadString(imageRef, "digest"));
            }

            return new MachineSnapshot(
                ReadString(element, "id"),
                ReadString(element, "state") ?? "unknown",
                ReadString(element, "region"),
                ReadTimestamp(element, "created_at"),
                image);
        }
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
}

using System.Text.Json;

namespace BreakMeIfYouCan.Api.Proof;

/// <summary>
/// Distinguishes "the request succeeded and there is nothing there" from "the request
/// failed". Conflating the two is how a missing attestation becomes a green tick.
/// </summary>
public sealed record GitHubJsonResult(bool Success, JsonDocument? Document);

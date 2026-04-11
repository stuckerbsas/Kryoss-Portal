using KryossApi.Data.Entities;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

/// <summary>
/// Policy outcome for a hardware-fingerprint check on a signed agent request.
/// </summary>
public enum HwidCheckResult
{
    /// <summary>The request is fine — either the hwid matched, was just
    /// backfilled, or the machine is still in the rollout window.</summary>
    Ok,

    /// <summary>The machine has a stored hwid and the header disagrees.
    /// Caller MUST reject the request with 401.</summary>
    Mismatch,

    /// <summary>The agent did not send X-Hwid at all and the machine has a
    /// stored hwid. During the rollout window this is logged as a warning
    /// but allowed. Once the fleet is fully upgraded, flip the policy to
    /// <see cref="Mismatch"/> (see security-baseline.md §Hardware binding).</summary>
    MissingHeader
}

public interface IHwidVerifier
{
    /// <summary>
    /// Compare a freshly-presented hwid header value against the stored
    /// <paramref name="machine"/> fingerprint. Backfills the machine record
    /// if it didn't have one yet — the FIRST hwid we ever see for a machine
    /// becomes the binding, not the hwid claimed at enrollment. (That's OK:
    /// the enrollment request is itself authenticated by a one-shot code.)
    /// </summary>
    HwidCheckResult Verify(Machine machine, string? presentedHwid);
}

public sealed class HwidVerifier : IHwidVerifier
{
    private readonly ILogger<HwidVerifier> _logger;

    public HwidVerifier(ILogger<HwidVerifier> logger) => _logger = logger;

    public HwidCheckResult Verify(Machine machine, string? presentedHwid)
    {
        var trimmed = presentedHwid?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (string.IsNullOrEmpty(machine.Hwid))
            {
                // Rollout window: old agent, unbound machine. Silent pass.
                return HwidCheckResult.Ok;
            }

            _logger.LogWarning(
                "Agent {AgentId} sent no X-Hwid but machine has stored hwid — rollout-window pass",
                machine.AgentId);
            return HwidCheckResult.MissingHeader;
        }

        if (string.IsNullOrEmpty(machine.Hwid))
        {
            // Backfill: first time we see a hwid for this machine. Store and
            // accept. The caller is responsible for SaveChanges.
            machine.Hwid = trimmed.Length > 128 ? trimmed[..128] : trimmed;
            _logger.LogInformation(
                "Backfilled hwid for machine {AgentId}", machine.AgentId);
            return HwidCheckResult.Ok;
        }

        if (!string.Equals(machine.Hwid, trimmed, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Hwid mismatch for machine {AgentId} — stored differs from presented",
                machine.AgentId);
            return HwidCheckResult.Mismatch;
        }

        return HwidCheckResult.Ok;
    }
}

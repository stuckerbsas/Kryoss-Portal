namespace KryossApi.Services.CopilotReadiness;

public record DimensionScores(
    decimal D1Labels,
    decimal D2Oversharing,
    decimal D3External,
    decimal D4ConditionalAccess,
    decimal D5ZeroTrust,
    decimal D6Purview,
    decimal Overall,
    string Verdict
);

public static class ScoringEngine
{
    // D1: Labeling coverage (% of files labeled)
    public static decimal ScoreD1Labels(decimal labeledPercent) =>
        labeledPercent >= 80 ? 5m
        : labeledPercent >= 60 ? 3m
        : labeledPercent >= 40 ? 2m
        : 1m;

    // D2: Oversharing (count of overshared files)
    public static decimal ScoreD2Oversharing(int oversharedCount) =>
        oversharedCount < 5  ? 5m
        : oversharedCount < 10 ? 3m
        : oversharedCount < 20 ? 2m
        : 1m;

    // D3: External sharing risk
    public static decimal ScoreD3External(int highRiskCount, int pendingCount) =>
        highRiskCount == 0 && pendingCount == 0   ? 5m
        : highRiskCount == 0 && pendingCount < 10 ? 4m
        : highRiskCount < 10                      ? 3m
        : highRiskCount < 50                      ? 2m
        : 1m;

    // D4: Conditional Access coverage (% of required policies in place)
    public static decimal ScoreD4ConditionalAccess(decimal coveragePercent) =>
        coveragePercent >= 90 ? 5m
        : coveragePercent >= 75 ? 4m
        : coveragePercent >= 60 ? 3m
        : coveragePercent >= 40 ? 2m
        : 1m;

    // D5: Zero Trust posture
    //   n = entraGaps + (defenderCritical * 2) + defenderWarning
    public static decimal ScoreD5ZeroTrust(int entraGaps, int defenderCritical, int defenderWarning)
    {
        int n = entraGaps + (defenderCritical * 2) + defenderWarning;
        return n == 0  ? 5m
            : n <= 2   ? 4m
            : n <= 5   ? 3m
            : n <= 8   ? 2m
            : 1m;
    }

    // D6: Purview gap count
    public static decimal ScoreD6Purview(int purviewGaps) =>
        purviewGaps == 0 ? 5m
        : purviewGaps <= 2 ? 4m
        : purviewGaps <= 5 ? 3m
        : purviewGaps <= 8 ? 2m
        : 1m;

    // Overall weighted score
    public static decimal ComputeOverall(
        decimal d1, decimal d2, decimal d3, decimal d4, decimal d5, decimal d6) =>
        (d1 * 0.25m) + (d2 * 0.25m) + (d3 * 0.20m) + (d4 * 0.15m) + (d5 * 0.10m) + (d6 * 0.05m);

    public static string ComputeVerdict(decimal overall) =>
        overall >= 4.0m ? "Ready"
        : overall >= 3.0m ? "Nearly Ready"
        : "Not Ready";

    public static DimensionScores Compute(
        decimal labeledPercent,
        int oversharedCount,
        int externalHighRisk,
        int externalPending,
        decimal caPercent,
        int entraGaps,
        int defenderCritical,
        int defenderWarning,
        int purviewGaps)
    {
        decimal d1 = ScoreD1Labels(labeledPercent);
        decimal d2 = ScoreD2Oversharing(oversharedCount);
        decimal d3 = ScoreD3External(externalHighRisk, externalPending);
        decimal d4 = ScoreD4ConditionalAccess(caPercent);
        decimal d5 = ScoreD5ZeroTrust(entraGaps, defenderCritical, defenderWarning);
        decimal d6 = ScoreD6Purview(purviewGaps);
        decimal overall = ComputeOverall(d1, d2, d3, d4, d5, d6);
        string verdict = ComputeVerdict(overall);

        return new DimensionScores(d1, d2, d3, d4, d5, d6, overall, verdict);
    }
}

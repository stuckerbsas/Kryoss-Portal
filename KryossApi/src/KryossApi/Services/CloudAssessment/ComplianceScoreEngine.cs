using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Services.CloudAssessment;

/// <summary>
/// Computes per-framework compliance scores from scan findings.
/// Joins findings → finding_control_mappings → framework_controls → frameworks,
/// then calculates passing/failing/unmapped counts and a percentage score.
/// </summary>
public static class ComplianceScoreEngine
{
    public static async Task<List<CloudAssessmentFrameworkScore>> RecomputeAsync(
        KryossDbContext db, Guid scanId)
    {
        var existing = await db.CloudAssessmentFrameworkScores
            .Where(s => s.ScanId == scanId)
            .ToListAsync();
        if (existing.Count > 0)
        {
            db.CloudAssessmentFrameworkScores.RemoveRange(existing);
            await db.SaveChangesAsync();
        }

        var scores = await ComputeAsync(db, scanId);
        if (scores.Count > 0)
        {
            db.CloudAssessmentFrameworkScores.AddRange(scores);
            await db.SaveChangesAsync();
        }
        return scores;
    }

    public static async Task<List<CloudAssessmentFrameworkScore>> ComputeAsync(
        KryossDbContext db, Guid scanId)
    {
        // Load all active frameworks with their controls.
        var frameworks = await db.CloudAssessmentFrameworks
            .Where(f => f.Active)
            .Include(f => f.Controls)
            .AsNoTracking()
            .ToListAsync();

        // Load scan findings (just the matching keys + status).
        var findings = await db.CloudAssessmentFindings
            .Where(f => f.ScanId == scanId)
            .Select(f => new { f.Area, f.Service, f.Feature, f.Status })
            .ToListAsync();

        // Build finding lookup by (area|service|feature) → best status.
        // A finding key can appear multiple times (unlikely but defensive).
        // Priority: success > warning > action_required > other.
        var findingLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in findings)
        {
            var key = $"{f.Area}|{f.Service}|{f.Feature}";
            if (!findingLookup.TryGetValue(key, out var existing))
            {
                findingLookup[key] = f.Status;
            }
            else
            {
                findingLookup[key] = BetterStatus(existing, f.Status);
            }
        }

        // Load all mappings grouped by framework_control_id.
        var mappings = await db.CloudAssessmentFindingControlMappings
            .AsNoTracking()
            .ToListAsync();

        var mappingsByControl = mappings
            .GroupBy(m => m.FrameworkControlId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var scores = new List<CloudAssessmentFrameworkScore>();

        foreach (var fw in frameworks)
        {
            int totalControls = fw.Controls.Count;
            int coveredControls = 0;
            int passingControls = 0;
            int failingControls = 0;
            int unmappedControls = 0;

            foreach (var ctrl in fw.Controls)
            {
                if (!mappingsByControl.TryGetValue(ctrl.Id, out var ctrlMappings) || ctrlMappings.Count == 0)
                {
                    unmappedControls++;
                    continue;
                }

                // A control is "covered" if at least one mapped finding exists in this scan.
                // It "passes" if at least one mapped finding has status=success and none are action_required.
                bool hasCoverage = false;
                bool hasSuccess = false;
                bool hasFailing = false;

                foreach (var mapping in ctrlMappings)
                {
                    var key = $"{mapping.Area}|{mapping.Service}|{mapping.Feature}";
                    if (findingLookup.TryGetValue(key, out var status))
                    {
                        hasCoverage = true;
                        if (IsSuccess(status))
                            hasSuccess = true;
                        if (IsFailing(status))
                            hasFailing = true;
                    }
                }

                if (!hasCoverage)
                {
                    unmappedControls++;
                    continue;
                }

                coveredControls++;
                if (hasFailing)
                    failingControls++;
                else if (hasSuccess)
                    passingControls++;
                else
                    passingControls++; // warning-only = partial pass, count as passing
            }

            decimal scorePct = totalControls > 0
                ? Math.Round((decimal)passingControls / totalControls * 100m, 2)
                : 0m;

            scores.Add(new CloudAssessmentFrameworkScore
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                FrameworkId = fw.Id,
                TotalControls = totalControls,
                CoveredControls = coveredControls,
                PassingControls = passingControls,
                FailingControls = failingControls,
                UnmappedControls = unmappedControls,
                ScorePct = scorePct,
                Grade = GradeFromPct(scorePct),
                ComputedAt = DateTime.UtcNow
            });
        }

        return scores;
    }

    private static bool IsSuccess(string status) =>
        string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailing(string status) =>
        string.Equals(status, "action_required", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Action Required", StringComparison.OrdinalIgnoreCase);

    private static string BetterStatus(string a, string b)
    {
        int Rank(string s) => IsSuccess(s) ? 3 : s.Contains("warning", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return Rank(a) >= Rank(b) ? a : b;
    }

    private static string GradeFromPct(decimal pct) => pct switch
    {
        >= 95m => "A+",
        >= 85m => "A",
        >= 75m => "B",
        >= 60m => "C",
        >= 40m => "D",
        _ => "F"
    };
}

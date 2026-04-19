using System.Globalization;
using KryossApi.Data;
using KryossApi.Data.Entities;
using KryossApi.Services.CloudAssessment.Helpers;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// CA-10 Mail Flow &amp; Email Security pipeline. Inspects per-verified-domain
/// DNS posture (SPF / DKIM / DMARC / MTA-STS / BIMI) and samples licensed
/// users for mailbox forwarding rules + shared-mailbox heuristics. Every
/// collector catches its own failures — partial results are persisted.
/// </summary>
public static class MailFlowPipeline
{
    private const int UserSampleSize = 200;
    private const string EmailSvc = "email";
    private const string MailFlowSvc = "mail_flow";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        IDnsLookup dns,
        Guid scanId,
        KryossDbContext db,
        ILogger log,
        CancellationToken ct)
    {
        if (graph is null)
        {
            return new PipelineResult { PipelineName = "mail_flow", Status = "skipped" };
        }

        var ins = new MailFlowInsights();
        var status = "ok";

        try
        {
            await CollectDomains(graph, dns, ins, log, ct);
            await CollectMailboxRisks(graph, ins, log, ct);
            CollectSharedMailboxes(ins);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MailFlow pipeline partial failure");
            status = "partial";
        }

        Persist(db, scanId, ins);

        var findings = MailFlowRecommendations.Generate(ins);
        var metrics = BuildMetrics(ins);

        return new PipelineResult
        {
            PipelineName = "mail_flow",
            Status = status,
            Findings = findings,
            Metrics = metrics,
            Insights = ins
        };
    }

    // ── Domain collection ──────────────────────────────────────────────

    private static async Task CollectDomains(
        GraphServiceClient graph, IDnsLookup dns, MailFlowInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Domains.GetAsync(cancellationToken: ct);
            var domains = resp?.Value ?? [];

            var targets = domains
                .Where(d => d.IsVerified == true
                            && !string.IsNullOrEmpty(d.Id)
                            && !d.Id!.EndsWith(".onmicrosoft.com", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var tasks = targets.Select(async d =>
            {
                try
                {
                    var r = await InspectDomain(dns, d.Id!, d.IsDefault == true, d.IsVerified == true, ct);
                    lock (ins.DomainResults) { ins.DomainResults.Add(r); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Domain inspection failed for {Domain}", d.Id);
                    lock (ins.DomainResults)
                    {
                        ins.DomainResults.Add(new DomainInspectionResult
                        {
                            Domain = d.Id!,
                            IsDefault = d.IsDefault == true,
                            IsVerified = d.IsVerified == true,
                            Score = 0m
                        });
                    }
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { throw; }
        catch (ODataError ex)
        {
            log.LogWarning("Domain collection failed: {Status} {Msg}", ex.ResponseStatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Domain collection failed");
        }
    }

    private static async Task<DomainInspectionResult> InspectDomain(
        IDnsLookup dns, string domain, bool isDefault, bool isVerified, CancellationToken ct)
    {
        var r = new DomainInspectionResult
        {
            Domain = domain,
            IsDefault = isDefault,
            IsVerified = isVerified
        };

        // SPF
        var rootTxt = await dns.GetTxtRecordsAsync(domain, ct);
        var spfRecords = rootTxt.Where(t => t.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)).ToList();
        if (spfRecords.Count > 0)
        {
            r.SpfRecord = spfRecords[0];
            r.SpfValid = true;
            r.SpfMechanism = ExtractSpfMechanism(r.SpfRecord);
            r.SpfLookupCount = CountSpfLookups(r.SpfRecord);
            if (spfRecords.Count > 1) r.SpfWarnings.Add("multiple_spf_records");
            if (r.SpfLookupCount > 10) r.SpfWarnings.Add("too_many_lookups");
        }
        else
        {
            r.SpfMechanism = "missing";
        }

        // DKIM (selector1/selector2 — M365 publishes as CNAME; fall back to TXT).
        var s1 = $"selector1._domainkey.{domain}";
        var s2 = $"selector2._domainkey.{domain}";
        var s1Cname = await dns.GetCnameAsync(s1, ct);
        var s2Cname = await dns.GetCnameAsync(s2, ct);
        var s1Present = !string.IsNullOrEmpty(s1Cname) || (await dns.GetTxtRecordsAsync(s1, ct)).Count > 0;
        var s2Present = !string.IsNullOrEmpty(s2Cname) || (await dns.GetTxtRecordsAsync(s2, ct)).Count > 0;
        r.DkimS1Present = s1Present;
        r.DkimS2Present = s2Present;
        if (s1Present) r.DkimSelectors.Add("selector1");
        if (s2Present) r.DkimSelectors.Add("selector2");

        // DMARC
        var dmarcTxt = await dns.GetTxtRecordsAsync($"_dmarc.{domain}", ct);
        var dmarc = dmarcTxt.FirstOrDefault(t => t.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        if (dmarc is not null)
        {
            r.DmarcRecord = dmarc;
            ParseDmarc(dmarc, r);
            r.DmarcValid = !string.IsNullOrEmpty(r.DmarcPolicy);
        }

        // MTA-STS (TXT only — full HTTPS policy fetch out of scope; treat TXT-present as "testing" default).
        var stsTxt = await dns.GetTxtRecordsAsync($"_mta-sts.{domain}", ct);
        var sts = stsTxt.FirstOrDefault(t => t.StartsWith("v=STSv1", StringComparison.OrdinalIgnoreCase));
        if (sts is not null)
        {
            r.MtaStsRecord = sts;
            r.MtaStsPolicy = "testing";
        }
        else
        {
            r.MtaStsPolicy = "missing";
        }

        // BIMI
        var bimiTxt = await dns.GetTxtRecordsAsync($"default._bimi.{domain}", ct);
        r.BimiPresent = bimiTxt.Any(t => t.StartsWith("v=BIMI1", StringComparison.OrdinalIgnoreCase));

        r.Score = ComputeDomainScore(r);
        return r;
    }

    private static string ExtractSpfMechanism(string record)
    {
        foreach (var token in new[] { "-all", "~all", "?all", "+all" })
        {
            if (record.Contains(token, StringComparison.OrdinalIgnoreCase)) return token;
        }
        return "none";
    }

    private static int CountSpfLookups(string record)
    {
        var tokens = record.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int count = 0;
        foreach (var t in tokens)
        {
            var lc = t.ToLowerInvariant();
            if (lc.StartsWith("include:")
                || lc.StartsWith("a:") || lc == "a"
                || lc.StartsWith("mx:") || lc == "mx"
                || lc.StartsWith("ptr:") || lc == "ptr"
                || lc.StartsWith("exists:")
                || lc.StartsWith("redirect="))
            {
                count++;
            }
        }
        return count;
    }

    private static void ParseDmarc(string record, DomainInspectionResult r)
    {
        foreach (var raw in record.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = raw.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var val = kv[1].Trim();
            switch (key)
            {
                case "p": r.DmarcPolicy = val.ToLowerInvariant(); break;
                case "sp": r.DmarcSubdomainPolicy = val.ToLowerInvariant(); break;
                case "pct":
                    if (int.TryParse(val, NumberStyles.Integer, Inv, out var pct)) r.DmarcPct = pct;
                    break;
                case "rua": r.DmarcRua = val; break;
                case "ruf": r.DmarcRuf = val; break;
            }
        }
        if (!r.DmarcPct.HasValue && !string.IsNullOrEmpty(r.DmarcPolicy)) r.DmarcPct = 100;
    }

    private static decimal ComputeDomainScore(DomainInspectionResult r)
    {
        decimal s = 0m;

        if (r.SpfValid && (r.SpfMechanism == "-all" || r.SpfMechanism == "~all") && r.SpfLookupCount <= 10) s += 2.0m;
        else if (r.SpfValid) s += 1.0m;

        if (r.DkimS1Present && r.DkimS2Present) s += 2.0m;
        else if (r.DkimS1Present || r.DkimS2Present) s += 1.0m;

        if (r.DmarcValid && (r.DmarcPolicy == "quarantine" || r.DmarcPolicy == "reject")) s += 3.0m;
        else if (r.DmarcValid && r.DmarcPolicy == "none") s += 1.5m;

        if (r.DmarcPct == 100 && r.DmarcPolicy != null && r.DmarcPolicy != "none") s += 1.0m;

        if (r.MtaStsPolicy == "enforce") s += 1.5m;
        else if (r.MtaStsPolicy == "testing") s += 0.75m;

        if (r.BimiPresent) s += 0.5m;

        return Math.Round(s, 1);
    }

    // ── Mailbox risk collection ────────────────────────────────────────

    private static async Task CollectMailboxRisks(
        GraphServiceClient graph, MailFlowInsights ins, ILogger log, CancellationToken ct)
    {
        List<Microsoft.Graph.Models.User> users;
        try
        {
            var resp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Top = UserSampleSize;
                rc.QueryParameters.Select = new[]
                {
                    "id", "userPrincipalName", "displayName", "accountEnabled",
                    "assignedLicenses", "mail"
                };
                rc.QueryParameters.Filter = "accountEnabled eq true";
            }, ct);
            users = resp?.Value ?? [];
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MailFlow user list failed");
            return;
        }

        ins.UsersSampled = users.Count;

        int processed = 0;
        foreach (var user in users)
        {
            if (processed >= UserSampleSize) break;
            processed++;

            if (string.IsNullOrEmpty(user.Id) || string.IsNullOrEmpty(user.UserPrincipalName)) continue;

            // Candidate shared mailbox heuristic: enabled + no license + has mail.
            if ((user.AssignedLicenses is null || user.AssignedLicenses.Count == 0)
                && !string.IsNullOrEmpty(user.Mail))
            {
                ins.SharedMailboxes.Add(new SharedMailbox
                {
                    MailboxUpn = user.UserPrincipalName!,
                    DisplayName = user.DisplayName,
                    HasPasswordEnabled = false
                });
            }

            try
            {
                var rulesResp = await graph.Users[user.Id!]
                    .MailFolders["inbox"]
                    .MessageRules
                    .GetAsync(cancellationToken: ct);

                var rules = rulesResp?.Value ?? [];
                foreach (var rule in rules)
                {
                    EvaluateRule(ins, user.UserPrincipalName!, user.DisplayName, rule);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (ODataError ex) when (ex.ResponseStatusCode == 403)
            {
                log.LogInformation("MailFlow: Mail.Read consent missing — skipping forwarding inspection");
                ins.MailReadConsented = false;
                return;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 404 || ex.ResponseStatusCode == 409)
            {
                // Inbox not provisioned for this user (shared/unlicensed). Skip silently.
            }
            catch (Exception ex)
            {
                log.LogDebug(ex, "Rule fetch failed for {Upn}", user.UserPrincipalName);
            }
        }
    }

    private static void EvaluateRule(
        MailFlowInsights ins, string upn, string? displayName, Microsoft.Graph.Models.MessageRule rule)
    {
        var actions = rule.Actions;
        if (actions is null) return;

        var forwardTargets = new List<string>();
        if (actions.ForwardTo is not null)
            forwardTargets.AddRange(actions.ForwardTo.Select(r => r.EmailAddress?.Address).Where(a => !string.IsNullOrEmpty(a))!);
        if (actions.RedirectTo is not null)
            forwardTargets.AddRange(actions.RedirectTo.Select(r => r.EmailAddress?.Address).Where(a => !string.IsNullOrEmpty(a))!);
        if (actions.ForwardAsAttachmentTo is not null)
            forwardTargets.AddRange(actions.ForwardAsAttachmentTo.Select(r => r.EmailAddress?.Address).Where(a => !string.IsNullOrEmpty(a))!);

        if (forwardTargets.Count == 0) return;

        var userDomain = ExtractDomain(upn);
        var stealth = actions.Delete == true;

        foreach (var target in forwardTargets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tDomain = ExtractDomain(target);
            var isExternal = !string.IsNullOrEmpty(userDomain)
                             && !string.IsNullOrEmpty(tDomain)
                             && !string.Equals(userDomain, tDomain, StringComparison.OrdinalIgnoreCase);

            ins.ForwardingRisks.Add(new MailboxRisk
            {
                UserPrincipalName = upn,
                DisplayName = displayName,
                RiskType = isExternal ? "external_forward" : "internal_forward",
                Severity = isExternal ? "high" : "medium",
                RiskDetail = rule.DisplayName,
                ForwardTarget = target
            });

            if (stealth)
            {
                ins.ForwardingRisks.Add(new MailboxRisk
                {
                    UserPrincipalName = upn,
                    DisplayName = displayName,
                    RiskType = "stealth_forward",
                    Severity = "high",
                    RiskDetail = rule.DisplayName,
                    ForwardTarget = target
                });
            }
        }
    }

    private static string? ExtractDomain(string? email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        var at = email.IndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : null;
    }

    private static void CollectSharedMailboxes(MailFlowInsights ins)
    {
        // Heuristic already populated during CollectMailboxRisks — no Graph call
        // distinguishes shared from user mailboxes without Exchange Online.
    }

    // ── Persistence ────────────────────────────────────────────────────

    private static void Persist(KryossDbContext db, Guid scanId, MailFlowInsights ins)
    {
        var now = DateTime.UtcNow;

        foreach (var d in ins.DomainResults)
        {
            db.CloudAssessmentMailDomains.Add(new CloudAssessmentMailDomain
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Domain = d.Domain,
                IsDefault = d.IsDefault,
                IsVerified = d.IsVerified,
                SpfRecord = d.SpfRecord,
                SpfValid = d.SpfValid,
                SpfMechanism = d.SpfMechanism,
                SpfLookupCount = d.SpfLookupCount,
                SpfWarnings = d.SpfWarnings.Count > 0 ? string.Join(",", d.SpfWarnings) : null,
                DkimS1Present = d.DkimS1Present,
                DkimS2Present = d.DkimS2Present,
                DkimSelectors = d.DkimSelectors.Count > 0 ? string.Join(",", d.DkimSelectors) : null,
                DmarcRecord = d.DmarcRecord,
                DmarcValid = d.DmarcValid,
                DmarcPolicy = d.DmarcPolicy,
                DmarcSubdomainPolicy = d.DmarcSubdomainPolicy,
                DmarcPct = d.DmarcPct,
                DmarcRua = d.DmarcRua,
                DmarcRuf = d.DmarcRuf,
                MtaStsRecord = d.MtaStsRecord,
                MtaStsPolicy = d.MtaStsPolicy,
                BimiPresent = d.BimiPresent,
                Score = d.Score,
                CreatedAt = now
            });
        }

        var dedupedRisks = ins.ForwardingRisks
            .GroupBy(r => (r.UserPrincipalName, r.RiskType))
            .Select(g => g.First())
            .ToList();
        foreach (var risk in dedupedRisks)
        {
            db.CloudAssessmentMailboxRisks.Add(new CloudAssessmentMailboxRisk
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                UserPrincipalName = risk.UserPrincipalName,
                DisplayName = risk.DisplayName,
                RiskType = risk.RiskType,
                RiskDetail = risk.RiskDetail,
                ForwardTarget = risk.ForwardTarget,
                Severity = risk.Severity,
                CreatedAt = now
            });
        }

        foreach (var sm in ins.SharedMailboxes)
        {
            db.CloudAssessmentSharedMailboxes.Add(new CloudAssessmentSharedMailbox
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                MailboxUpn = sm.MailboxUpn,
                DisplayName = sm.DisplayName,
                DelegatesCount = sm.DelegatesCount,
                FullAccessUsers = sm.FullAccessUsers.Count > 0 ? string.Join(",", sm.FullAccessUsers) : null,
                SendAsUsers = sm.SendAsUsers.Count > 0 ? string.Join(",", sm.SendAsUsers) : null,
                HasPasswordEnabled = sm.HasPasswordEnabled,
                LastActivity = sm.LastActivity,
                CreatedAt = now
            });
        }
    }

    private static Dictionary<string, string> BuildMetrics(MailFlowInsights ins)
    {
        var dr = ins.DomainResults;
        var avg = dr.Count > 0 ? dr.Average(d => d.Score) : 0m;
        return new Dictionary<string, string>
        {
            ["domains_total"] = dr.Count.ToString(Inv),
            ["domains_verified"] = dr.Count(d => d.IsVerified).ToString(Inv),
            ["spf_valid"] = dr.Count(d => d.SpfValid && (d.SpfMechanism == "-all" || d.SpfMechanism == "~all")).ToString(Inv),
            ["dkim_full"] = dr.Count(d => d.DkimS1Present && d.DkimS2Present).ToString(Inv),
            ["dmarc_enforced"] = dr.Count(d => d.DmarcValid && (d.DmarcPolicy == "quarantine" || d.DmarcPolicy == "reject")).ToString(Inv),
            ["mta_sts_enforce"] = dr.Count(d => d.MtaStsPolicy == "enforce").ToString(Inv),
            ["bimi_present"] = dr.Count(d => d.BimiPresent).ToString(Inv),
            ["forwarding_risks_total"] = ins.ForwardingRisks.Count.ToString(Inv),
            ["forwarding_external"] = ins.ForwardingRisks.Count(r => r.RiskType == "external_forward").ToString(Inv),
            ["forwarding_stealth"] = ins.ForwardingRisks.Count(r => r.RiskType == "stealth_forward").ToString(Inv),
            ["shared_mailboxes_total"] = ins.SharedMailboxes.Count.ToString(Inv),
            ["avg_domain_score"] = avg.ToString("F1", Inv)
        };
    }
}

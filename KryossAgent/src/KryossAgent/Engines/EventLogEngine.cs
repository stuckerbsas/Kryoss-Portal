using System.Diagnostics.Eventing.Reader;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Reads Windows Event Log configuration and query summaries.
///
/// Replaces fragile wevtutil shell-out parsing with the in-box
/// <see cref="EventLogConfiguration"/> + <see cref="EventLogReader"/>
/// APIs. Both are AOT-safe (no reflection, no dynamic code).
///
/// Supported <c>CheckType</c> values:
///   max_size      -> long bytes (MaximumSizeInBytes)
///   retention     -> string  ("Circular" / "AutoBackup" / "Retain")
///   last_cleared  -> ISO8601 string of latest clearing event (1102 for
///                    Security, 104 for System/Application), or null
///   event_count   -> int count of a given EventID in the last 24h
///                    (EventID read from <c>ValueName</c>)
///   latest_event  -> ISO8601 of the most recent event matching
///                    <c>ValueName</c> (EventID), or null
///
/// Required ControlDef fields: <c>LogName</c> and <c>CheckType</c>.
/// For count_*/latest_* variants, <c>ValueName</c> must hold the numeric
/// EventID as a string.
/// </summary>
public class EventLogEngine : ICheckEngine
{
    public string Type => "eventlog";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);
        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control));
        }
        return results;
    }

    private static CheckResult ExecuteOne(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        if (string.IsNullOrEmpty(control.LogName))
        {
            result.Exists = false;
            result.Value = "ERROR: logName is required";
            return result;
        }

        var checkType = control.CheckType ?? "max_size";

        try
        {
            switch (checkType)
            {
                case "max_size":
                    using (var cfg = new EventLogConfiguration(control.LogName))
                    {
                        result.Exists = true;
                        result.Value = cfg.MaximumSizeInBytes;
                    }
                    break;

                case "retention":
                    using (var cfg = new EventLogConfiguration(control.LogName))
                    {
                        result.Exists = true;
                        result.Value = cfg.LogMode.ToString(); // Circular, AutoBackup, Retain
                    }
                    break;

                case "last_cleared":
                {
                    var clearingId = string.Equals(control.LogName, "Security", StringComparison.OrdinalIgnoreCase)
                        ? 1102 : 104;
                    var latest = QueryLatestEvent(control.LogName, clearingId);
                    result.Exists = latest is not null;
                    result.Value = latest?.ToString("o");
                    break;
                }

                case "event_count":
                {
                    if (!int.TryParse(control.ValueName, out var eid))
                    {
                        result.Exists = false;
                        result.Value = "ERROR: valueName must be numeric EventID";
                        break;
                    }
                    var count = CountEventsLast24h(control.LogName, eid);
                    result.Exists = true;
                    result.Value = count;
                    break;
                }

                case "latest_event":
                {
                    if (!int.TryParse(control.ValueName, out var eid))
                    {
                        result.Exists = false;
                        result.Value = "ERROR: valueName must be numeric EventID";
                        break;
                    }
                    var latest = QueryLatestEvent(control.LogName, eid);
                    result.Exists = latest is not null;
                    result.Value = latest?.ToString("o");
                    break;
                }

                default:
                    result.Exists = false;
                    result.Value = $"ERROR: unknown checkType '{checkType}'";
                    break;
            }
        }
        catch (EventLogNotFoundException)
        {
            result.Exists = false;
            result.Value = null;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Exists = null;
            result.Value = $"ERROR: access denied reading '{control.LogName}': {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Exists = null;
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }

    private static DateTime? QueryLatestEvent(string logName, int eventId)
    {
        var xpath = $"*[System[EventID={eventId}]]";
        var q = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(q);
        using var evt = reader.ReadEvent();
        return evt?.TimeCreated;
    }

    private static int CountEventsLast24h(string logName, int eventId)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24).ToString("o");
        var xpath = $"*[System[EventID={eventId} and TimeCreated[@SystemTime>='{cutoff}']]]";
        var q = new EventLogQuery(logName, PathType.LogName, xpath);
        using var reader = new EventLogReader(q);

        int count = 0;
        const int MaxCount = 10_000; // safety cap
        while (count < MaxCount)
        {
            using var evt = reader.ReadEvent();
            if (evt is null) break;
            count++;
        }
        return count;
    }
}

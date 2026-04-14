using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Xml;
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
                    // v1.5.1: prefer EventIds array + Days window. Fall back
                    // to legacy ValueName + 24h window for existing controls.
                    int[] eids;
                    int days;
                    if (control.EventIds is { Length: > 0 })
                    {
                        eids = control.EventIds;
                        days = control.Days ?? 1;
                    }
                    else if (int.TryParse(control.ValueName, out var legacyEid))
                    {
                        eids = new[] { legacyEid };
                        days = 1;
                    }
                    else
                    {
                        result.Exists = false;
                        result.Value = "ERROR: eventIds (array) or valueName (int) required";
                        break;
                    }

                    var count = CountEvents(control.LogName, eids, days);
                    result.Exists = true;
                    result.Value = count;
                    break;
                }

                case "event_top_sources":
                {
                    // v1.5.1: Aggregate events by a payload field and return
                    // the top N sources as "value1:count1|value2:count2|..."
                    if (control.EventIds is null || control.EventIds.Length == 0
                        || string.IsNullOrEmpty(control.PayloadField))
                    {
                        result.Exists = false;
                        result.Value = "ERROR: eventIds + payloadField required";
                        break;
                    }

                    var days = control.Days ?? 90;
                    var topN = control.TopN ?? 10;
                    var top = GetTopEventSources(
                        control.LogName, control.EventIds, days,
                        control.PayloadField, topN);

                    result.Exists = true;
                    result.Value = top;
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
        => CountEvents(logName, new[] { eventId }, 1);

    /// <summary>
    /// Count events matching any of the given EventIDs in the last N days.
    /// </summary>
    private static int CountEvents(string logName, int[] eventIds, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("o");
        var idFilter = eventIds.Length == 1
            ? $"EventID={eventIds[0]}"
            : $"({string.Join(" or ", eventIds.Select(e => $"EventID={e}"))})";
        var xpath = $"*[System[{idFilter} and TimeCreated[@SystemTime>='{cutoff}']]]";

        EventLogQuery q;
        try
        {
            q = new EventLogQuery(logName, PathType.LogName, xpath);
        }
        catch
        {
            return 0;
        }

        using var reader = new EventLogReader(q);
        int count = 0;
        const int MaxCount = 500_000; // 90-day windows can be large — higher cap
        while (count < MaxCount)
        {
            EventRecord? evt;
            try { evt = reader.ReadEvent(); }
            catch { break; }
            if (evt is null) break;
            using (evt) { count++; }
        }
        return count;
    }

    /// <summary>
    /// Aggregate events by a specific EventData payload field (e.g. UserName,
    /// WorkstationName, ClientName) and return the top N as a
    /// pipe-separated "value:count|value:count|..." string.
    ///
    /// Parses the event XML directly (AOT-safe, no reflection).
    /// </summary>
    private static string GetTopEventSources(string logName, int[] eventIds,
        int days, string payloadField, int topN)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("o");
        var idFilter = eventIds.Length == 1
            ? $"EventID={eventIds[0]}"
            : $"({string.Join(" or ", eventIds.Select(e => $"EventID={e}"))})";
        var xpath = $"*[System[{idFilter} and TimeCreated[@SystemTime>='{cutoff}']]]";

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        EventLogQuery q;
        try
        {
            q = new EventLogQuery(logName, PathType.LogName, xpath);
        }
        catch
        {
            return "";
        }

        using var reader = new EventLogReader(q);
        int scanned = 0;
        const int MaxScan = 200_000;

        while (scanned < MaxScan)
        {
            EventRecord? evt;
            try { evt = reader.ReadEvent(); }
            catch { break; }
            if (evt is null) break;

            using (evt)
            {
                scanned++;
                var value = ExtractPayloadField(evt, payloadField);
                if (string.IsNullOrEmpty(value)) continue;
                counts[value] = counts.TryGetValue(value, out var c) ? c + 1 : 1;
            }
        }

        return string.Join("|", counts
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    /// <summary>
    /// Extract a named EventData field from an EventRecord XML without
    /// reflection. Walks the XML and returns the first Data element whose
    /// Name attribute matches.
    /// </summary>
    private static string? ExtractPayloadField(EventRecord evt, string fieldName)
    {
        string xml;
        try { xml = evt.ToXml(); }
        catch { return null; }
        if (string.IsNullOrEmpty(xml)) return null;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml));
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element
                    && reader.LocalName == "Data"
                    && reader.GetAttribute("Name") == fieldName)
                {
                    return reader.ReadElementContentAsString();
                }
            }
        }
        catch { /* malformed xml — skip */ }
        return null;
    }
}

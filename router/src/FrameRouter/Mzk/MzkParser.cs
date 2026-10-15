using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace FrameRouter.Mzk;

// Parses the undocumented XHR responses of MZK Zielona Góra's eInfo site (rj.mzk.zgora.pl).
internal static class MzkParser
{
    public static StopBoard ParseTimetable(string xml, int stopId)
    {
        var stop = XDocument.Parse(xml).Root?.Element("Stop")
            ?? throw new FormatException($"MZK timetable for stop {stopId} has no <Stop> element.");

        var departures = stop.Descendants("R")
            .SelectMany(route => route.Elements("S").Select(slot => ParseDeparture(stopId, route, slot)))
            .OrderBy(departure => departure.Seconds)
            .ToList();

        return new StopBoard
        {
            StopId = stopId,
            StopName = (string?)stop.Attribute("name") ?? string.Empty,
            Notice = NullIfBlank((string?)stop.Attribute("ds")),
            Departures = departures,
        };
    }

    // Each row is an array: [busStopId, name, sign number, ...unused fields].
    public static IReadOnlyList<StopMatch> ParseStopList(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 3)
            .Select(row => new StopMatch
            {
                StopId = row[0].GetInt32(),
                Name = row[1].GetString() ?? string.Empty,
                Number = row[2].GetString() ?? string.Empty,
            })
            .ToList();
    }

    private static Departure ParseDeparture(int stopId, XElement route, XElement slot) => new()
    {
        StopId = stopId,
        Line = ((string?)route.Attribute("nr") ?? string.Empty).Trim(),
        Direction = (string?)route.Attribute("dir") ?? string.Empty,
        Time = (string?)slot.Attribute("t") ?? string.Empty,
        Seconds = int.TryParse((string?)slot.Attribute("s"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : int.MaxValue,
        Live = string.Equals((string?)slot.Attribute("veh"), "T", StringComparison.Ordinal),
        Date = NullIfBlank((string?)slot.Attribute("uw")),
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

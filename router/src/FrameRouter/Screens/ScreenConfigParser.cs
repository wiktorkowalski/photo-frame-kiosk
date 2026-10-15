using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameRouter.Screens;

internal sealed record ScreenConfigParseResult(ScreenConfig? Config, IReadOnlyList<string> Errors);

internal static class ScreenConfigParser
{
    public const int MaxDepartureLimit = 20;
    public const int MaxFrameIdLength = 40;
    public const string TimeFormat = "HH:mm";

    // The file is edited by hand on GitHub: comments and trailing commas are fine, a misspelled key is not.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static readonly IReadOnlyDictionary<string, DayOfWeek> Days = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday,
    };

    public static ScreenConfigParseResult Parse(string json)
    {
        ScreenConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ScreenConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return new ScreenConfigParseResult(null, [ex.Message]);
        }

        if (config is null)
            return new ScreenConfigParseResult(null, ["The config is empty."]);

        var errors = Validate(config);
        return new ScreenConfigParseResult(errors.Count == 0 ? config : null, errors);
    }

    public static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    public static bool IsValidFrameId(string frameId) =>
        frameId.Length is > 0 and <= MaxFrameIdLength
        && frameId.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static List<string> Validate(ScreenConfig config)
    {
        List<string> errors = [];

        if (config.Screens.Count == 0)
            errors.Add("\"screens\" is empty.");

        if (config.Frames.Count == 0)
            errors.Add("\"frames\" is empty. Add one entry per frame, for example \"kitchen\".");

        foreach (var (name, screen) in config.Screens)
            errors.AddRange(ValidateScreen(name, screen));

        foreach (var (frameId, plan) in config.Frames)
            errors.AddRange(ValidateFrame(frameId, plan, config.Screens));

        return errors;
    }

    private static IEnumerable<string> ValidateScreen(string name, ScreenDefinition screen)
    {
        if (!ScreenTypes.All.Contains(screen.Type))
        {
            yield return $"Screen \"{name}\": unknown type \"{screen.Type}\". Use one of: {string.Join(", ", ScreenTypes.All)}.";
            yield break;
        }

        if (screen.Type == ScreenTypes.Departures)
        {
            if (screen.Stops is not { Count: > 0 })
                yield return $"Screen \"{name}\": \"stops\" needs at least one MZK busStopId.";
            else if (screen.Stops.Any(id => id <= 0))
                yield return $"Screen \"{name}\": every stop id must be a positive number.";

            if (screen.Limit is < 1 or > MaxDepartureLimit)
                yield return $"Screen \"{name}\": \"limit\" must be between 1 and {MaxDepartureLimit}.";
        }

        if (screen.Type == ScreenTypes.Url && !IsHttpUrl(screen.Url))
            yield return $"Screen \"{name}\": \"url\" must be an absolute http or https address.";
    }

    private static IEnumerable<string> ValidateFrame(string frameId, FramePlan plan, Dictionary<string, ScreenDefinition> screens)
    {
        var label = $"Frame \"{frameId}\"";

        if (!IsValidFrameId(frameId))
            yield return $"{label}: use only lowercase letters, digits and \"-\" in a frame id.";

        if (!screens.ContainsKey(plan.Default))
            yield return $"{label}: \"default\" names screen \"{plan.Default}\", which is not in \"screens\".";

        for (var i = 0; i < plan.Schedule.Count; i++)
        {
            foreach (var error in ValidateRule($"{label}, schedule rule {i + 1}", plan.Schedule[i], screens))
                yield return error;
        }
    }

    private static IEnumerable<string> ValidateRule(string label, ScheduleRule rule, Dictionary<string, ScreenDefinition> screens)
    {
        if (!screens.ContainsKey(rule.Screen))
            yield return $"{label}: screen \"{rule.Screen}\" is not in \"screens\".";

        var fromValid = TimeOnly.TryParseExact(rule.From, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from);
        var toValid = TimeOnly.TryParseExact(rule.To, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to);
        if (!fromValid || !toValid)
            yield return $"{label}: \"from\" and \"to\" must be times like \"06:30\".";
        else if (from == to)
            yield return $"{label}: \"from\" and \"to\" are the same time.";

        foreach (var day in rule.Days ?? [])
        {
            if (!Days.ContainsKey(day))
                yield return $"{label}: unknown day \"{day}\". Use mon, tue, wed, thu, fri, sat, sun.";
        }
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

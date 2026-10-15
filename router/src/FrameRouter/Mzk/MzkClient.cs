using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace FrameRouter.Mzk;

internal sealed class MzkClient(HttpClient httpClient, IMemoryCache cache)
{
    public static readonly Uri BaseAddress = new("https://rj.mzk.zgora.pl/");

    // MZK's own page polls every 10 s; frames sharing a stop share one request per window.
    private static readonly TimeSpan TimetableCacheDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopListCacheDuration = TimeSpan.FromHours(6);

    public async Task<StopBoard> GetTimetableAsync(int stopId, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(("mzk-timetable", stopId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimetableCacheDuration;
            var xml = await httpClient.GetStringAsync($"TimeTable/GetTimetableReal?busStopId={stopId.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
            return MzkParser.ParseTimetable(xml, stopId);
        }))!;

    public async Task<IReadOnlyList<StopMatch>> SearchStopsAsync(string query, CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(("mzk-stops", query), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StopListCacheDuration;
            var json = await httpClient.GetStringAsync($"BusStops/GetMapBusStopListJson?q={Uri.EscapeDataString(query)}&tId=0", cancellationToken);
            return MzkParser.ParseStopList(json);
        }))!;
}

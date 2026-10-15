using FrameRouter.Mzk;

namespace FrameRouter.Tests;

public sealed class MzkParserTests
{
    // Trimmed real responses from rj.mzk.zgora.pl, 2026-10-15.
    private const string Timetable = """
        <Schedules time="14:11" xmlns="">
          <Stop name="Barcikowiczki" id="987" ds=" Objazd ul. Wrocławska ">
            <Day type="RO" desc="ROBOCZY SZKOLNY">
              <R nr="27" dir="WYSPIAŃSKIEGO" vt="A" vuw="">
                <S th="16" tm="13" t="16:13" m="3" s="7283" id="2439" nb="0" veh="N" uw="" kuw="W   " />
              </R>
              <R nr="8" dir="OS. ŚLĄSKIE" vt="A" vuw="N">
                <S th="" tm="6 min" t="6 min" m="2" s="372" id="1025" nb="454" veh="T" uw="" kuw="    " />
              </R>
              <R nr="27" dir="DWORZEC GŁÓWNY" vt="A" vuw="">
                <S th="06" tm="39" t="06:39" m="3" s="59243" id="2436" nb="0" veh="N" uw="2026-10-16" kuw="W   " />
              </R>
            </Day>
          </Stop>
        </Schedules>
        """;

    [Fact]
    public void ParseTimetable_RealResponse_SortsBySecondsAndMapsFields()
    {
        var board = MzkParser.ParseTimetable(Timetable, 987);

        Assert.Equal("Barcikowiczki", board.StopName);
        Assert.Equal("Objazd ul. Wrocławska", board.Notice);
        Assert.Equal(["8", "27", "27"], board.Departures.Select(departure => departure.Line));

        var live = board.Departures[0];
        Assert.True(live.Live);
        Assert.Equal("6 min", live.Time);
        Assert.Equal(372, live.Seconds);
        Assert.Null(live.Date);

        var tomorrow = board.Departures[2];
        Assert.False(tomorrow.Live);
        Assert.Equal("2026-10-16", tomorrow.Date);
    }

    [Fact]
    public void ParseTimetable_NoStopElement_Throws() =>
        Assert.Throws<FormatException>(() => MzkParser.ParseTimetable("<Schedules />", 1));

    [Fact]
    public void ParseStopList_RealResponse_ReadsIdNameAndNumber()
    {
        const string json = """[[11 ,"Dworzec Główny","279",0,15.5166,51.94716,0,"A",0,1,0,""],[28 ,"Dworzec Główny","400",0,15.51,51.94,0,"A",0,1,0,""]]""";

        var stops = MzkParser.ParseStopList(json);

        Assert.Equal(2, stops.Count);
        Assert.Equal(new StopMatch { StopId = 11, Name = "Dworzec Główny", Number = "279" }, stops[0]);
    }
}

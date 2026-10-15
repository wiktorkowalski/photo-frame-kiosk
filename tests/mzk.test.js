import assert from 'node:assert/strict';
import { test } from 'node:test';
import { buildBoard, parseStopList, parseTimetable, toStopOption } from '../worker/src/mzk.js';

// Trimmed real responses from rj.mzk.zgora.pl, 2026-10-15.
const TIMETABLE = `<Schedules time="14:11" xmlns="">
  <Stop name="Barcikowiczki" id="987" ds=" Objazd ul. Wrocławska ">
    <Day type="RO" desc="ROBOCZY SZKOLNY">
      <R nr="27" dir="WYSPIAŃSKIEGO" vt="A" vuw="">
        <S th="16" tm="13" t="16:13" m="3" s="7283" id="2439" nb="0" veh="N" uw="" kuw="W   " />
      </R>
      <R nr="8" dir="ZAWADZKIEGO &quot;ZOŚKI&quot;" vt="A" vuw="N">
        <S th="" tm="6 min" t="6 min" m="2" s="372" id="1025" nb="454" veh="T" uw="" kuw="    " />
      </R>
      <R nr="27" dir="DWORZEC GŁÓWNY" vt="A" vuw="">
        <S th="06" tm="39" t="06:39" m="3" s="59243" id="2436" nb="0" veh="N" uw="2026-10-16" kuw="W   " />
      </R>
    </Day>
  </Stop>
</Schedules>`;

test('parseTimetable sorts by seconds and maps the fields', () => {
  const board = parseTimetable(TIMETABLE, 987);

  assert.equal(board.stopName, 'Barcikowiczki');
  assert.equal(board.notice, 'Objazd ul. Wrocławska');
  assert.deepEqual(board.departures.map((departure) => departure.line), ['8', '27', '27']);

  const [live, , tomorrow] = board.departures;
  assert.deepEqual(live, {
    stopId: 987,
    line: '8',
    direction: 'ZAWADZKIEGO "ZOŚKI"',
    time: '6 min',
    seconds: 372,
    live: true,
    date: null,
  });
  assert.equal(tomorrow.live, false);
  assert.equal(tomorrow.date, '2026-10-16');
});

test('parseTimetable rejects a response without a stop', () => {
  assert.throws(() => parseTimetable('<Schedules />', 1), /no <Stop>/);
});

test('parseStopList reads id, name and sign number', () => {
  const stops = parseStopList('[[11 ,"Dworzec Główny","279",0,15.5166,51.94716,0,"A",0,1,0,""],[28 ,"Dworzec Główny","400",0,15.51,51.94,0,"A",0,1,0,""]]');
  assert.deepEqual(stops[0], { stopId: 11, name: 'Dworzec Główny', number: '279' });
  assert.equal(stops.length, 2);
});

const board = (stopId, ...seconds) => ({
  stopId,
  stopName: `Stop ${stopId}`,
  notice: null,
  departures: seconds.map((value) => ({ stopId, line: String(value), direction: 'X', time: 't', seconds: value, live: false, date: null })),
});

test('buildBoard merges stops, sorts and applies the limit', () => {
  const result = buildBoard([11, 28], [board(11, 300, 60), board(28, 120)], 2, 'now');
  assert.deepEqual(result.departures.map((departure) => departure.seconds), [60, 120]);
  assert.deepEqual(result.failedStops, []);
});

test('buildBoard lists a stop that failed', () => {
  const result = buildBoard([11, 28], [board(11, 60), null], 8, 'now');
  assert.deepEqual(result.failedStops, [28]);
  assert.equal(result.stops.length, 1);
});

test('toStopOption lists unique lines and directions', () => {
  const option = toStopOption({ stopId: 987, name: 'Barcikowiczki', number: '1' }, parseTimetable(TIMETABLE, 987));
  assert.deepEqual(option.lines, ['8', '27']);
  assert.equal(option.directions.length, 3);
});

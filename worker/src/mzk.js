// Parses the undocumented XHR responses of MZK Zielona Góra's eInfo site (rj.mzk.zgora.pl).
// Workers have no DOMParser; the XML is flat and machine-made, so attribute regexes are enough.

const STOP_PATTERN = /<Stop\b([^>]*)>/;
const ROUTE_PATTERN = /<R\b([^>]*)>([\s\S]*?)<\/R>/g;
const SLOT_PATTERN = /<S\b([^>]*?)\/?>/g;
const ATTRIBUTE_PATTERN = /([A-Za-z_][\w-]*)="([^"]*)"/g;

const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };

function decode(value) {
  return value.replace(/&(#x[0-9a-f]+|#\d+|\w+);/gi, (match, entity) => {
    if (entity[0] === '#') {
      const code = entity[1] === 'x' || entity[1] === 'X' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10);
      return Number.isFinite(code) && code >= 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match;
    }
    return ENTITIES[entity.toLowerCase()] ?? match;
  });
}

function attributes(text) {
  const result = {};
  for (const match of text.matchAll(ATTRIBUTE_PATTERN)) result[match[1]] = decode(match[2]);
  return result;
}

function blankToNull(value) {
  return value && value.trim() ? value.trim() : null;
}

export function parseTimetable(xml, stopId) {
  const stopMatch = STOP_PATTERN.exec(xml);
  if (!stopMatch) throw new Error(`MZK timetable for stop ${stopId} has no <Stop> element.`);
  const stop = attributes(stopMatch[1]);

  const departures = [];
  for (const route of xml.matchAll(ROUTE_PATTERN)) {
    const routeAttributes = attributes(route[1]);
    for (const slot of route[2].matchAll(SLOT_PATTERN)) {
      const slotAttributes = attributes(slot[1]);
      const seconds = Number.parseInt(slotAttributes.s, 10);
      departures.push({
        stopId,
        line: (routeAttributes.nr || '').trim(),
        direction: routeAttributes.dir || '',
        // MZK's own display text: "3 min", "<1min" or a clock time like "14:43".
        time: slotAttributes.t || '',
        seconds: Number.isFinite(seconds) ? seconds : Number.MAX_SAFE_INTEGER,
        // A GPS-tracked vehicle runs the course, so the time is live, not timetable.
        live: slotAttributes.veh === 'T',
        // Set when the departure is not today, as yyyy-MM-dd.
        date: blankToNull(slotAttributes.uw),
      });
    }
  }
  departures.sort((a, b) => a.seconds - b.seconds);

  return { stopId, stopName: stop.name || '', notice: blankToNull(stop.ds), departures };
}

// Each row is an array: [busStopId, name, sign number, ...unused fields].
export function parseStopList(json) {
  const rows = JSON.parse(json);
  if (!Array.isArray(rows)) throw new Error('MZK stop list is not an array.');
  return rows
    .filter((row) => Array.isArray(row) && row.length >= 3 && Number.isInteger(row[0]))
    .map((row) => ({ stopId: row[0], name: String(row[1] ?? ''), number: String(row[2] ?? '') }));
}

export function buildBoard(stopIds, boards, limit, fetchedAt) {
  const loaded = boards.filter(Boolean);
  return {
    stops: loaded.map((board) => ({ stopId: board.stopId, stopName: board.stopName, notice: board.notice })),
    departures: loaded
      .flatMap((board) => board.departures)
      .sort((a, b) => a.seconds - b.seconds)
      .slice(0, limit),
    failedStops: stopIds.filter((stopId) => !loaded.some((board) => board.stopId === stopId)),
    fetchedAt,
  };
}

export function toStopOption(match, board) {
  const unique = (values) => [...new Set(values)];
  return {
    stopId: match.stopId,
    name: match.name,
    number: match.number,
    lines: board ? unique(board.departures.map((departure) => departure.line)) : [],
    directions: board ? unique(board.departures.map((departure) => departure.direction)) : [],
  };
}

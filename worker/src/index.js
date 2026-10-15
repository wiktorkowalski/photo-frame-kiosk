// MZK Zielona Góra proxy for the frame page. MZK sends no CORS headers, so a browser on
// github.io cannot call it directly. Public, read-only, cached at the Cloudflare edge.
import { buildBoard, parseStopList, parseTimetable, toStopOption } from './mzk.js';

const MZK_BASE = 'https://rj.mzk.zgora.pl/';
const USER_AGENT = 'photo-frame-kiosk (+https://github.com/wiktorkowalski/photo-frame-kiosk)';

// MZK's own page polls every 10 s; frames sharing a stop share one upstream request per window.
const TIMETABLE_CACHE_SECONDS = 15;
const STOP_SEARCH_CACHE_SECONDS = 600;
const MZK_TIMEOUT_MS = 10000;

const DEFAULT_LIMIT = 8;
const MAX_LIMIT = 20;
const MAX_STOPS_PER_REQUEST = 6;
const MAX_STOP_OPTIONS = 12;
const MIN_QUERY_LENGTH = 2;
const MAX_QUERY_LENGTH = 40;

const CORS_HEADERS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, OPTIONS',
  'Access-Control-Max-Age': '86400',
};

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS_HEADERS, 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}

async function fetchMzk(path, cacheSeconds) {
  const response = await fetch(MZK_BASE + path, {
    headers: { 'User-Agent': USER_AGENT },
    signal: AbortSignal.timeout(MZK_TIMEOUT_MS),
    // MZK answers no-store; cacheEverything overrides that at the edge.
    cf: { cacheTtl: cacheSeconds, cacheEverything: true },
  });
  if (!response.ok) throw new Error(`MZK answered HTTP ${response.status} for ${path}`);
  return response.text();
}

async function tryTimetable(stopId) {
  try {
    return parseTimetable(await fetchMzk(`TimeTable/GetTimetableReal?busStopId=${stopId}`, TIMETABLE_CACHE_SECONDS), stopId);
  } catch (error) {
    console.warn(`MZK timetable for stop ${stopId} failed: ${error.message}`);
    return null;
  }
}

function parseStopIds(value) {
  const parts = (value || '').split(',').map((part) => part.trim()).filter(Boolean);
  const ids = parts.map((part) => (/^\d{1,6}$/.test(part) ? Number(part) : NaN));
  return ids.every((id) => id > 0) ? [...new Set(ids)] : null;
}

async function departures(url) {
  const stopIds = parseStopIds(url.searchParams.get('stops'));
  if (!stopIds || stopIds.length === 0 || stopIds.length > MAX_STOPS_PER_REQUEST) {
    return json({ error: `Pass 1 to ${MAX_STOPS_PER_REQUEST} stop ids, like ?stops=11,28.` }, 400);
  }
  const requested = Number.parseInt(url.searchParams.get('limit') || '', 10);
  const limit = Number.isFinite(requested) ? Math.min(Math.max(requested, 1), MAX_LIMIT) : DEFAULT_LIMIT;

  const boards = await Promise.all(stopIds.map(tryTimetable));
  const board = buildBoard(stopIds, boards, limit, new Date().toISOString());
  return board.stops.length === 0 ? json({ error: 'MZK did not answer for any of the stops.' }, 502) : json(board);
}

async function stops(url) {
  const query = (url.searchParams.get('q') || '').trim();
  if (query.length < MIN_QUERY_LENGTH || query.length > MAX_QUERY_LENGTH) {
    return json({ error: `Type ${MIN_QUERY_LENGTH} to ${MAX_QUERY_LENGTH} letters of the stop name.` }, 400);
  }

  let matches;
  try {
    const path = `BusStops/GetMapBusStopListJson?q=${encodeURIComponent(query.toLowerCase())}&tId=0`;
    matches = parseStopList(await fetchMzk(path, STOP_SEARCH_CACHE_SECONDS)).slice(0, MAX_STOP_OPTIONS);
  } catch (error) {
    console.warn(`MZK stop search for "${query}" failed: ${error.message}`);
    return json({ error: 'MZK stop search failed.' }, 502);
  }

  // Lines and directions are not in the stop list; only a timetable call shows which way a post goes.
  const boards = await Promise.all(matches.map((match) => tryTimetable(match.stopId)));
  return json(matches.map((match, index) => toStopOption(match, boards[index])));
}

export default {
  async fetch(request) {
    if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: CORS_HEADERS });
    if (request.method !== 'GET') return json({ error: 'Only GET.' }, 405);

    const url = new URL(request.url);
    switch (url.pathname) {
      case '/departures':
        return departures(url);
      case '/stops':
        return stops(url);
      case '/healthz':
        return json({ ok: true });
      default:
        return json({ error: 'Not found. Use /departures?stops=11 or /stops?q=dworzec.' }, 404);
    }
  },
};

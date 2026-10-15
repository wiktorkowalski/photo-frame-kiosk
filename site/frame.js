// Frame page. Target: Android System WebView 106 (Chrome 106), 1 GB RAM. No build step:
// keep to ES2020 and to APIs Chrome 106 has.
import { isValidFrameId, localNow, resolveDimLevel, resolveScreenName, TIME_ZONE } from './schedule.js';

// tools/build-site.mjs replaces 'dev' with the hash of the page code and adds ?v=<hash> to every
// script URL, so a reload after a deploy fetches the new code past the 10 min Pages cache.
const CODE_VERSION = 'dev';

const CONFIG_POLL_MS = 60000;
const SCHEDULE_TICK_MS = 15000;
const FETCH_TIMEOUT_MS = 15000;
const OFFLINE_NOTICE_MS = 180000;
const DEPARTURES_REFRESH_MS = 20000;
const DEPARTURES_STALE_MS = 90000;
const RELOAD_RETRY_MS = 300000;
// Pages may serve the old index.html for up to 10 min after a deploy: allow one update reload per window.
const UPDATE_RELOAD_GAP_MS = 600000;
const UPDATE_RELOAD_KEY = 'frame-update-reload-at';
const NIGHTLY_RELOAD_HOUR = 4; // a daily reload keeps WebView memory in check on 1 GB frames
const CONFIG_CACHE_KEY = 'frame-config';

const root = document.getElementById('screen');
const statusBox = document.getElementById('status');
const dimLayer = document.getElementById('dim');
const params = new URLSearchParams(location.search);
const frameId = params.get('frame') || '';

let config = null;
let lastConfigAt = 0;
let currentKey = null;
let disposeScreen = function () {};

const timeFormat = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });
const dateFormat = new Intl.DateTimeFormat('pl-PL', { weekday: 'long', day: 'numeric', month: 'long', timeZone: TIME_ZONE });

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function setStatus(text) {
  statusBox.hidden = !text;
  statusBox.textContent = text || '';
}

// A hung request must not stall the next refresh.
async function fetchJson(url) {
  const controller = new AbortController();
  const timer = setTimeout(function () { controller.abort(); }, FETCH_TIMEOUT_MS);
  try {
    const response = await fetch(url, { cache: 'no-store', signal: controller.signal });
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return await response.json();
  } finally {
    clearTimeout(timer);
  }
}

// The query string defeats the 10 min GitHub Pages CDN cache, so a commit shows up within a minute.
function fetchConfig() {
  return fetchJson('config.json?t=' + Date.now());
}

function readCachedConfig() {
  try {
    return JSON.parse(localStorage.getItem(CONFIG_CACHE_KEY));
  } catch (error) {
    return null;
  }
}

function cacheConfig(value) {
  try {
    localStorage.setItem(CONFIG_CACHE_KEY, JSON.stringify(value));
  } catch (error) {
    // Storage off or full: the frame still works, only an offline start shows a message.
  }
}

function show(key, render) {
  if (key === currentKey) return;
  disposeScreen();
  disposeScreen = function () {};
  root.textContent = '';
  try {
    disposeScreen = render(root) || function () {};
  } catch (error) {
    // Without this a renderer error leaves the screen blank: the next tick sees the same key and skips.
    root.textContent = '';
    root.appendChild(el('div', 'message', 'Błąd ekranu: ' + error.message));
  }
  currentKey = key;
}

function reloadForUpdate() {
  let last = 0;
  try {
    last = Number(sessionStorage.getItem(UPDATE_RELOAD_KEY)) || 0;
    if (Date.now() - last < UPDATE_RELOAD_GAP_MS) return;
    sessionStorage.setItem(UPDATE_RELOAD_KEY, String(Date.now()));
  } catch (error) {
    // No sessionStorage: skip the update reload; the 04:00 reload brings the new code.
    return;
  }
  location.reload();
}

function showMessage(text) {
  show('message:' + text, function (host) {
    host.appendChild(el('div', 'message', text));
  });
}

function mzkApi() {
  return (params.get('mzk') || config.mzkApi || '').replace(/\/$/, '');
}

function applySchedule() {
  if (!config) return;
  const plan = config.frames[frameId];
  if (!plan) {
    dimLayer.style.opacity = '0';
    showMessage('Ramka „' + frameId + '” nie ma wpisu w screens.json.');
    return;
  }

  const now = localNow(new Date());
  // Dim lives outside the screen key: a change must not reload the iframe.
  dimLayer.style.opacity = String(resolveDimLevel(plan, now));

  const name = resolveScreenName(plan, now);
  const screen = config.screens[name];
  const renderer = screen && renderers[screen.type];
  if (!renderer) {
    showMessage('Ekran „' + name + '” ma nieznany typ. Strona przeładuje się po aktualizacji.');
    return;
  }
  show(name + ':' + JSON.stringify(screen), function (host) {
    return renderer(host, screen);
  });
}

async function refreshConfig() {
  try {
    const next = await fetchConfig();
    if (CODE_VERSION !== 'dev' && next.version !== CODE_VERSION) reloadForUpdate();
    config = next;
    lastConfigAt = Date.now();
    cacheConfig(next);
    setStatus(null);
  } catch (error) {
    if (!config) config = readCachedConfig();
    if (!config) {
      showMessage('Brak połączenia. Ramka spróbuje ponownie za minutę.');
      return;
    }
    if (Date.now() - lastConfigAt > OFFLINE_NOTICE_MS) setStatus('Brak połączenia z siecią');
  }
  applySchedule();
}

// Screens. Each renderer fills the host and returns a cleanup function.

function renderClock(host) {
  const box = el('div', 'clock');
  const time = el('div', 'clock-time');
  const date = el('div', 'clock-date');
  box.appendChild(time);
  box.appendChild(date);
  host.appendChild(box);

  function tick() {
    const now = new Date();
    time.textContent = timeFormat.format(now);
    date.textContent = dateFormat.format(now);
  }

  tick();
  const timer = setInterval(tick, 1000);
  return function () { clearInterval(timer); };
}

function renderDepartures(host, screen) {
  const box = el('div', 'departures');
  const header = el('div', 'dep-header');
  const title = el('div', 'dep-title', screen.title || '');
  const clock = el('div', 'dep-clock');
  const notice = el('div', 'dep-notice');
  const list = el('div', 'dep-list');
  const footer = el('div', 'dep-footer', 'Ładowanie odjazdów…');
  notice.hidden = true;
  header.appendChild(title);
  header.appendChild(clock);
  box.appendChild(header);
  box.appendChild(notice);
  box.appendChild(list);
  box.appendChild(footer);
  host.appendChild(box);

  const url = mzkApi() + '/departures?stops=' + screen.stops.join(',') + '&limit=' + (screen.limit || 8);
  let lastSuccessAt = 0;
  let disposed = false;

  function drawRows(board) {
    list.textContent = '';
    if (board.departures.length === 0) list.appendChild(el('div', 'message', 'Brak odjazdów'));
    board.departures.forEach(function (departure) {
      const row = el('div', 'dep-row');
      row.appendChild(el('div', 'dep-line', departure.line));
      row.appendChild(el('div', 'dep-dir', departure.direction));
      const time = el('div', 'dep-time');
      if (departure.live) time.appendChild(el('span', 'dep-live'));
      // date is yyyy-MM-dd and only set when the departure is not today.
      const day = departure.date ? ' (' + departure.date.slice(8, 10) + '.' + departure.date.slice(5, 7) + ')' : '';
      time.appendChild(document.createTextNode(departure.time + day));
      row.appendChild(time);
      list.appendChild(row);
    });

    if (!screen.title) title.textContent = board.stops.map(function (stop) { return stop.stopName; }).join(' · ');
    const notices = board.stops.map(function (stop) { return stop.notice; }).filter(Boolean);
    if (board.failedStops.length) notices.push('Brak danych dla przystanku ' + board.failedStops.join(', '));
    notice.hidden = notices.length === 0;
    notice.textContent = notices.join(' · ');
  }

  function drawFooter() {
    clock.textContent = timeFormat.format(new Date());
    if (!lastSuccessAt) return;
    const stale = Date.now() - lastSuccessAt > DEPARTURES_STALE_MS;
    footer.className = stale ? 'dep-footer stale' : 'dep-footer';
    footer.textContent = (stale ? 'Brak świeżych danych MZK · ostatnie z ' : 'MZK Zielona Góra · ') +
      timeFormat.format(new Date(lastSuccessAt)) + ' · ● na żywo (GPS)';
  }

  async function load() {
    try {
      const board = await fetchJson(url);
      if (disposed) return;
      lastSuccessAt = Date.now();
      drawRows(board);
    } catch (error) {
      if (!lastSuccessAt) footer.textContent = 'Nie udało się pobrać odjazdów (' + error.message + ')';
    }
    drawFooter();
  }

  load();
  const refreshTimer = setInterval(load, DEPARTURES_REFRESH_MS);
  const clockTimer = setInterval(drawFooter, 1000);
  return function () {
    disposed = true;
    clearInterval(refreshTimer);
    clearInterval(clockTimer);
  };
}

function renderUrl(host, screen) {
  const frame = el('iframe', 'embed');
  frame.src = screen.url;
  frame.setAttribute('allow', 'autoplay; fullscreen');
  host.appendChild(frame);
}

const renderers = {
  clock: renderClock,
  departures: renderDepartures,
  url: renderUrl,
};

// Reload only when the site answers: a reload into a dead link leaves WebView on its
// error page, where no script runs to recover.
async function reloadWhenReachable() {
  try {
    await fetchConfig();
    location.reload();
  } catch (error) {
    setTimeout(reloadWhenReachable, RELOAD_RETRY_MS);
  }
}

function scheduleNightlyReload() {
  const now = new Date();
  const next = new Date(now);
  next.setHours(NIGHTLY_RELOAD_HOUR, 0, 0, 0);
  if (next <= now) next.setDate(next.getDate() + 1);
  setTimeout(reloadWhenReachable, next - now);
}

if (isValidFrameId(frameId)) {
  refreshConfig();
  setInterval(refreshConfig, CONFIG_POLL_MS);
  setInterval(applySchedule, SCHEDULE_TICK_MS);
  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible') refreshConfig();
  });
  scheduleNightlyReload();
} else {
  showMessage('Dodaj do adresu ?frame=<id>, na przykład ?frame=dxmart.');
}

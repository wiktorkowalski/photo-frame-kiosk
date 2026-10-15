// Frame page. Target: Android System WebView 106 (Chrome 106), 1 GB RAM. No build step:
// keep to ES2020 and to APIs Chrome 106 has.
(function () {
  'use strict';

  const TIME_ZONE = 'Europe/Warsaw';
  const SILENCE_LIMIT_MS = 70000; // the server sends the screen every 25 s
  const WATCHDOG_INTERVAL_MS = 10000;
  const POLL_INTERVAL_MS = 300000;
  const DEPARTURES_REFRESH_MS = 20000;
  const DEPARTURES_STALE_MS = 90000;
  const FETCH_TIMEOUT_MS = 15000;
  const RELOAD_RETRY_MS = 300000;
  const NIGHTLY_RELOAD_HOUR = 4; // a daily reload keeps WebView memory in check on 1 GB frames

  const root = document.getElementById('screen');
  const statusBox = document.getElementById('status');
  const dimLayer = document.getElementById('dim');
  const frameId = new URLSearchParams(location.search).get('frame') || '';

  let loadedVersion = null;
  let currentKey = null;
  let disposeScreen = function () {};
  let source = null;
  let lastMessageAt = 0;

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

  function show(key, render) {
    if (key === currentKey) return;
    disposeScreen();
    disposeScreen = function () {};
    root.textContent = '';
    try {
      disposeScreen = render(root) || function () {};
    } catch (error) {
      // Without this a renderer error leaves the screen blank: the next snapshot has the same key and is skipped.
      root.textContent = '';
      root.appendChild(el('div', 'message', 'Błąd ekranu: ' + error.message));
    }
    currentKey = key;
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

  function showMessage(text) {
    show('message:' + text, function (host) {
      host.appendChild(el('div', 'message', text));
    });
  }

  function applySnapshot(snapshot) {
    lastMessageAt = Date.now();
    setStatus(null);

    if (loadedVersion === null) {
      loadedVersion = snapshot.version;
    } else if (snapshot.version !== loadedVersion) {
      location.reload();
      return;
    }

    // Dim lives outside the screen key: a change must not reload the iframe.
    dimLayer.style.opacity = String(snapshot.dim || 0);

    if (!snapshot.screen) {
      showMessage('Ramka „' + frameId + '” nie ma wpisu w screens.json.');
      return;
    }

    const renderer = renderers[snapshot.screen.type];
    if (!renderer) {
      showMessage('Nieznany typ ekranu: ' + snapshot.screen.type);
      return;
    }

    show(snapshot.name + ':' + JSON.stringify(snapshot.screen), function (host) {
      return renderer(host, snapshot.screen);
    });
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

    const url = '/api/departures?stops=' + screen.stops.join(',') + '&limit=' + (screen.limit || 8);
    let lastSuccessAt = 0;
    let disposed = false;

    function drawRows(board) {
      list.textContent = '';
      if (board.departures.length === 0) {
        list.appendChild(el('div', 'message', 'Brak odjazdów'));
      }
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

  // Connection: SSE first, with a watchdog and a slow poll as a fallback.

  function connect() {
    if (source) source.close();
    lastMessageAt = Date.now();
    source = new EventSource('/api/frames/' + encodeURIComponent(frameId) + '/stream');
    source.addEventListener('screen', function (event) {
      applySnapshot(JSON.parse(event.data));
    });
    source.onerror = function () {
      setStatus('Brak połączenia z serwerem');
    };
  }

  async function poll() {
    try {
      applySnapshot(await fetchJson('/api/frames/' + encodeURIComponent(frameId) + '/screen'));
    } catch (error) {
      setStatus('Brak połączenia z serwerem');
    }
  }

  function watchdog() {
    // EventSource gives up for good after an HTTP error; a silent stream also means a dead link.
    if (Date.now() - lastMessageAt > SILENCE_LIMIT_MS || (source && source.readyState === EventSource.CLOSED)) {
      connect();
      poll();
    }
  }

  // Reload only when the router answers: a reload into a dead link leaves WebView on its
  // error page, where no script runs to recover.
  async function reloadWhenReachable() {
    try {
      const controller = new AbortController();
      const timer = setTimeout(function () { controller.abort(); }, FETCH_TIMEOUT_MS);
      const response = await fetch('/healthz', { cache: 'no-store', signal: controller.signal });
      clearTimeout(timer);
      if (response.ok) {
        location.reload();
        return;
      }
    } catch (error) {
      // Router or Wi-Fi is down: try again later.
    }
    setTimeout(reloadWhenReachable, RELOAD_RETRY_MS);
  }

  function scheduleNightlyReload() {
    const now = new Date();
    const next = new Date(now);
    next.setHours(NIGHTLY_RELOAD_HOUR, 0, 0, 0);
    if (next <= now) next.setDate(next.getDate() + 1);
    setTimeout(reloadWhenReachable, next - now);
  }

  if (!/^[a-z0-9-]{1,40}$/.test(frameId)) {
    showMessage('Dodaj do adresu ?frame=<id>, na przykład ?frame=dxmart.');
    return;
  }

  connect();
  setInterval(watchdog, WATCHDOG_INTERVAL_MS);
  setInterval(poll, POLL_INTERVAL_MS);
  document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'visible') {
      poll();
      watchdog();
    }
  });
  scheduleNightlyReload();
})();

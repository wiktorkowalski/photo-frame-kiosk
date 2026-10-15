// Control page: show a screen on a frame for a while, then the frame returns to its schedule.
(function () {
  'use strict';

  const TOKEN_KEY = 'frame-control-token';
  const REFRESH_MS = 15000;

  const login = document.getElementById('login');
  const tokenInput = document.getElementById('token');
  const panel = document.getElementById('panel');
  const framesBox = document.getElementById('frames');
  const minutesSelect = document.getElementById('minutes');
  const message = document.getElementById('message');
  const timeFormat = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Warsaw' });

  let busy = false;
  let sessionToken = readToken();

  function readToken() {
    try { return localStorage.getItem(TOKEN_KEY) || ''; } catch (error) { return ''; }
  }

  function writeToken(value) {
    try {
      if (value) localStorage.setItem(TOKEN_KEY, value);
      else localStorage.removeItem(TOKEN_KEY);
    } catch (error) { /* private mode: the token lives until the tab closes */ }
    sessionToken = value;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function setMessage(text, isError) {
    message.textContent = text || '';
    message.className = isError ? 'error' : 'muted';
  }

  function showLogin(text) {
    writeToken('');
    panel.hidden = true;
    login.hidden = false;
    setMessage(text, Boolean(text));
    tokenInput.focus();
  }

  async function call(method, path, body) {
    const response = await fetch('/api/control' + path, {
      method: method,
      cache: 'no-store',
      headers: Object.assign({ Authorization: 'Bearer ' + sessionToken }, body ? { 'Content-Type': 'application/json' } : {}),
      body: body ? JSON.stringify(body) : undefined,
    });
    if (response.status === 401) {
      showLogin('Zły token.');
      return null;
    }
    if (!response.ok) throw new Error(await errorText(response));
    return response.json();
  }

  // The API answers errors as a JSON string or as problem+json.
  async function errorText(response) {
    const text = await response.text();
    try {
      const body = JSON.parse(text);
      return typeof body === 'string' ? body : body.detail || body.title || text;
    } catch (error) {
      return text || 'HTTP ' + response.status;
    }
  }

  // The server notices an ended override on its next 15 s tick; do not show a past end time meanwhile.
  function isOverridden(snapshot) {
    return Boolean(snapshot.overrideUntil) && new Date(snapshot.overrideUntil) > new Date();
  }

  function describe(snapshot) {
    if (isOverridden(snapshot)) {
      return el('div', 'state override', 'Teraz: ' + snapshot.name + ' do ' + timeFormat.format(new Date(snapshot.overrideUntil)));
    }
    const dim = snapshot.dim > 0 ? ' · przyciemniona' : '';
    return el('div', 'state muted', 'Teraz: ' + snapshot.name + ' (harmonogram)' + dim);
  }

  function render(state) {
    framesBox.textContent = '';
    if (state.frames.length === 0) framesBox.appendChild(el('p', 'muted', 'Brak ramek w screens.json.'));

    state.frames.forEach(function (frame) {
      const card = el('div', 'frame');
      const title = el('h2', null, frame.label || frame.id);
      if (frame.label) title.appendChild(el('span', 'muted', ' ' + frame.id));
      card.appendChild(title);
      card.appendChild(describe(frame.snapshot));

      const buttons = el('div', 'screens');
      state.screens.forEach(function (screen) {
        const button = el('button', screen === frame.snapshot.name ? 'current' : null, screen);
        button.type = 'button';
        button.addEventListener('click', function () {
          act('PUT', frame.id, { screen: screen, minutes: Number(minutesSelect.value) });
        });
        buttons.appendChild(button);
      });
      card.appendChild(buttons);

      if (isOverridden(frame.snapshot)) {
        const back = el('button', 'back', 'Wróć do harmonogramu');
        back.type = 'button';
        back.addEventListener('click', function () { act('DELETE', frame.id); });
        card.appendChild(back);
      }

      framesBox.appendChild(card);
    });
  }

  async function refresh() {
    if (!sessionToken || busy || document.hidden) return;
    try {
      const state = await call('GET', '/frames');
      // An action started while this request was in flight; its own refresh draws the result.
      if (!state || busy) return;
      login.hidden = true;
      panel.hidden = false;
      setMessage('Zmiana dociera do ramki w kilka sekund.');
      render(state);
    } catch (error) {
      setMessage('Błąd: ' + error.message, true);
    }
  }

  async function act(method, frameId, body) {
    busy = true;
    framesBox.querySelectorAll('button').forEach(function (button) { button.disabled = true; });
    let failure = null;
    try {
      await call(method, '/frames/' + encodeURIComponent(frameId) + '/override', body);
    } catch (error) {
      failure = error;
    } finally {
      busy = false;
    }
    // The refresh re-enables the buttons; the error must stay visible after it.
    await refresh();
    if (failure) setMessage('Błąd: ' + failure.message, true);
  }

  login.addEventListener('submit', function (event) {
    event.preventDefault();
    writeToken(tokenInput.value.trim());
    tokenInput.value = '';
    refresh();
  });

  document.getElementById('logout').addEventListener('click', function () { showLogin(''); });
  document.addEventListener('visibilitychange', function () { if (!document.hidden) refresh(); });
  setInterval(refresh, REFRESH_MS);

  if (sessionToken) refresh();
  else showLogin('');
})();

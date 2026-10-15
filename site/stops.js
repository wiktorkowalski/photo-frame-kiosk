const form = document.getElementById('search');
const query = document.getElementById('query');
const results = document.getElementById('results');
const override = new URLSearchParams(location.search).get('mzk');

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

async function mzkApi() {
  if (override) return override.replace(/\/$/, '');
  const response = await fetch('config.json?t=' + Date.now(), { cache: 'no-store' });
  const config = await response.json();
  return (config.mzkApi || '').replace(/\/$/, '');
}

form.addEventListener('submit', async function (event) {
  event.preventDefault();
  results.textContent = 'Szukam…';
  try {
    const response = await fetch((await mzkApi()) + '/stops?q=' + encodeURIComponent(query.value), { cache: 'no-store' });
    const body = await response.json();
    if (!response.ok) throw new Error(body.error || 'HTTP ' + response.status);
    results.textContent = body.length ? '' : 'Nic nie znaleziono.';
    body.forEach(function (stop) {
      const card = el('div', 'stop');
      const head = el('div');
      head.appendChild(el('span', 'stop-id', 'id ' + stop.stopId));
      head.appendChild(document.createTextNode(' · ' + stop.name + ' (słupek ' + stop.number + ')'));
      card.appendChild(head);
      card.appendChild(el('div', 'muted', 'Linie: ' + (stop.lines.join(', ') || '—')));
      card.appendChild(el('div', 'muted', 'Kierunki: ' + (stop.directions.join(', ') || '—')));
      results.appendChild(card);
    });
  } catch (error) {
    results.textContent = 'Błąd: ' + error.message;
  }
});

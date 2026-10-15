(function () {
  'use strict';

  const form = document.getElementById('search');
  const query = document.getElementById('query');
  const results = document.getElementById('results');

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  form.addEventListener('submit', async function (event) {
    event.preventDefault();
    results.textContent = 'Szukam…';
    try {
      const response = await fetch('/api/stops?q=' + encodeURIComponent(query.value), { cache: 'no-store' });
      if (!response.ok) throw new Error(await response.text());
      const stops = await response.json();
      results.textContent = stops.length ? '' : 'Nic nie znaleziono.';
      stops.forEach(function (stop) {
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
})();

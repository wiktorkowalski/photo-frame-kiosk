# photo-frame-kiosk

Tanie ramki Frameo jako ekrany ścienne. Każda ramka pokazuje jedną stałą stronę z tego repo:
`https://wiktorkowalski.github.io/photo-frame-kiosk/?frame=<id>`. Co jest na stronie, decyduje `screens.json`.

Strona działa w każdej sieci z internetem. Nie potrzebuje homelaba.

## Zmiana ekranu

1. Zmień [`screens.json`](screens.json) (także w przeglądarce na GitHubie, z telefonu).
2. Zrób commit na `master`.
3. Workflow **Pages** sprawdza plik i publikuje stronę. Ramki pobierają konfigurację co minutę,
   więc przełączają się w około 2 minuty.

Błędny plik daje czerwony przebieg **Pages** z listą błędów. Strona się wtedy nie zmienia, a ramki zostają przy ostatnim dobrym pliku.

Typy ekranów:

| `type` | Pola | Co pokazuje |
|---|---|---|
| `clock` | — | Godzina i data |
| `departures` | `stops` (id przystanków MZK), `title`, `limit` (1–20, domyślnie 8) | Najbliższe odjazdy MZK Zielona Góra, na żywo z GPS |
| `url` | `url` (tylko https) | Dowolna strona w ramce `<iframe>`. Strona z `X-Frame-Options` lub CSP `frame-ancestors` bez `https://wiktorkowalski.github.io` zostaje pusta. |

Id przystanku znajdziesz na <https://wiktorkowalski.github.io/photo-frame-kiosk/stops.html>. Każdy słupek ma własne id i własne kierunki.

Każda ramka w `frames` ma `default` i opcjonalny `schedule`. Pierwsza pasująca reguła wygrywa (czas Europe/Warsaw).
Reguła z `from` późniejszym niż `to` przechodzi przez północ, a `days` oznacza dzień jej startu.

### Przyciemnianie

Ramka nie usypia ekranu, bo Wi-Fi ramki nie wraca po uśpieniu ekranu Androida. Zamiast tego strona kładzie czarną warstwę:

```jsonc
"dxmart": {
  "default": "clock",
  "dim": [{ "from": "23:00", "to": "06:30", "level": 0.85 }]
}
```

- `level` to krycie warstwy: od powyżej 0 do 1. `0.85` zostawia 15 % jasności, `1` daje pełną czerń.
- `from`, `to` i `days` działają jak w `schedule`. Pierwsza pasująca reguła wygrywa.
- Dotyk przechodzi przez warstwę. Zmiana przyciemnienia nie przeładowuje ekranu, np. strony HA w `<iframe>`.

### Home Assistant

Ekran `ha` w `screens.json` pokazuje dashboard `dashboard-kiosk`. Działa tylko na ramkach w sieci domowej:
`ha.home.vicio.ovh` jest dostępny tylko w LAN. Wymagania:

- Traefik na homelab dodaje do `ha.home.vicio.ovh` nagłówek `Content-Security-Policy: frame-ancestors`
  z adresem strony ramki (middleware `ha-frame-ancestors` w `/home/ubuntu/docker/traefik/dynamic/middlewares.yml`).
  Przeglądarka ignoruje wtedy `X-Frame-Options` z traefika i z HA. Config HA zostaje bez zmian.
- Logowanie: raz, dotykiem na ramce, z zaznaczonym „Keep me logged in”. Użyj osobnego użytkownika HA bez uprawnień admina.
  HA trzyma token w `localStorage` ramki, więc logowanie przeżywa przeładowania strony.
  Wyczyszczenie danych aplikacji kiosku wymaga ponownego logowania.
- `trusted_networks` nie działa: middleware `ha-headers` usuwa `X-Forwarded-For`, więc HA widzi adres traefika, nie ramki.

Pierwsze logowanie: ustaw `"default": "ha"` dla ramki, zaloguj się na ramce, potem ustaw plan według potrzeb.

## Układ repo

- `site/` — strona ramki i wyszukiwarka przystanków. Czysty JS (moduły ES2020) bez kroku budowania,
  pisany pod WebView 106 (Chrome 106): bez zagnieżdżania CSS i nowszych API.
  `site/schedule.js` ma walidację `screens.json` i harmonogram; używa go strona i build.
- `screens.json` — konfiguracja ekranów i ramek. Komentarze `//` i `/* */` oraz przecinki na końcu list są dozwolone.
- `tools/build-site.mjs` — sprawdza `screens.json` i składa stronę z `config.json` do `_site/`.
- `worker/` — Cloudflare Worker `frame-mzk`: pośrednik do API MZK. MZK nie wysyła nagłówków CORS,
  więc przeglądarka ramki nie może pytać MZK wprost.
- `scripts/set-kiosk-url.sh` — ustawia adres kiosku na ramce przez ADB.
- `docs/new-frame.md` — przygotowanie kolejnej ramki.

## Jak to działa

- Strona co minutę pobiera `config.json` (z parametrem, który omija 10-minutowy cache Pages) i co 15 s liczy ekran z harmonogramu.
- Ostatnia dobra konfiguracja leży w `localStorage`. Gdy sieć padnie przy otwartej stronie, ramka dalej pokazuje plan i harmonogram.
  Restart ramki bez sieci kończy się stroną błędu WebView, aż kiosk ponowi ładowanie.
- `config.json` ma wersję kodu strony. Po zmianie kodu strona przeładowuje się sama (najwyżej raz na 10 min,
  bo Pages trzyma stary `index.html` do 10 min). Skrypty mają w adresie `?v=<wersja>`, więc cache WebView ich nie podmienia.
  Codziennie o 4:00 przeładowuje się też dla porządku w pamięci, ale tylko gdy strona odpowiada.
- Odjazdy idą przez Worker. Worker prosi Cloudflare o 15 s cache odpowiedzi MZK; log Workera (`MZK edge cache status`) pokazuje, czy działa.
  Worker przyjmuje zapytania tylko ze strony ramki (nagłówek `Origin`) i bez `Origin` (curl).

## Wdrożenie

- **Pages** (`.github/workflows/pages.yml`): każdy push na `master`. Testy, sprawdzenie `screens.json`, publikacja.
  Adres Workera bierze ze zmiennej repo `MZK_API_URL`. Bez niej build z ekranem `departures` kończy się błędem.
  Źródło Pages w ustawieniach repo: **GitHub Actions**.
- **Worker** (`.github/workflows/worker.yml`): push zmieniający `worker/`. Wymaga sekretów repo
  `CLOUDFLARE_API_TOKEN` (uprawnienie Workers Scripts: Edit) i `CLOUDFLARE_ACCOUNT_ID`.

Lokalnie (Node 20+):

```sh
npm test
npm run dev
```

Strona: <http://localhost:8080/?frame=dxmart&mzk=http://localhost:8787>. Parametr `mzk` wskazuje lokalny Worker.

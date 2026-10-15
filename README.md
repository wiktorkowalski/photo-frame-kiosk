# photo-frame-kiosk

Tanie ramki Frameo jako ekrany ścienne. Każda ramka pokazuje jedną stałą stronę:
`https://frame.vicio.ovh/?frame=<id>`. Co jest na tej stronie, decyduje `screens.json` w tym repo.

- `frame.vicio.ovh` działa z każdej sieci: Cloudflare Tunnel do routera na homelabie (`cloudflared` obok kontenera).
- `frame.home.vicio.ovh` działa tylko w sieci domowej, przez lokalny traefik. Nie zależy od Cloudflare.

## Zmiana ekranu

1. Zmień [`screens.json`](screens.json) (także w przeglądarce na GitHubie, z telefonu).
2. Zrób commit na `master`.
3. Workflow **Push screens** wysyła plik do routera. Ramki przełączają się w około 30 s.

Router sprawdza plik. Błędny plik daje czerwony przebieg z opisem błędu, a ramki zostają przy ostatnim dobrym pliku.

Typy ekranów:

| `type` | Pola | Co pokazuje |
|---|---|---|
| `clock` | — | Godzina i data |
| `departures` | `stops` (id przystanków MZK), `title`, `limit` (1–20, domyślnie 8) | Najbliższe odjazdy MZK Zielona Góra, na żywo z GPS |
| `url` | `url` (tylko https) | Dowolna strona w ramce `<iframe>`. Strona z `X-Frame-Options` lub CSP `frame-ancestors` bez adresu strony ramki zostaje pusta. |

Id przystanku znajdziesz na <https://frame.vicio.ovh/stops.html>. Każdy słupek ma własne id i własne kierunki.

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

Ekran `ha` w `screens.json` pokazuje dashboard `dashboard-kiosk`. Wymagania:

- Traefik na homelab dodaje do `ha.home.vicio.ovh` nagłówek `Content-Security-Policy: frame-ancestors 'self' https://frame.home.vicio.ovh https://frame.vicio.ovh`
  (middleware `ha-frame-ancestors` w `/home/ubuntu/docker/traefik/dynamic/middlewares.yml`).
  Przeglądarka ignoruje wtedy `X-Frame-Options` z traefika i z HA. Config HA zostaje bez zmian.
- Logowanie: raz, dotykiem na ramce, z zaznaczonym „Keep me logged in”. Użyj osobnego użytkownika HA bez uprawnień admina.
  HA trzyma token w `localStorage` ramki, więc logowanie przeżywa przeładowania strony.
  Wyczyszczenie danych aplikacji kiosku wymaga ponownego logowania.
- `trusted_networks` nie działa: middleware `ha-headers` usuwa `X-Forwarded-For`, więc HA widzi adres traefika, nie ramki.

Pierwsze logowanie: ustaw `"default": "ha"` dla ramki, zaloguj się na ramce, potem ustaw plan według potrzeb.

## Układ repo

- `router/` — aplikacja .NET 10 (minimal API) i strona ramki. Strona to czysty JS bez kroku budowania,
  pisany pod WebView 106 (Chrome 106): ES2020, bez zagnieżdżania CSS.
- `screens.json` — konfiguracja ekranów i ramek.
- `scripts/set-kiosk-url.sh` — ustawia adres kiosku na ramce przez ADB.
- `docs/new-frame.md` — przygotowanie kolejnej ramki.
- `deploy/compose.yml` — kopia pliku compose z hosta homelab.

## Jak to działa

- Strona ramki słucha zmian przez SSE (`/api/frames/<id>/stream`). Serwer wysyła ekran co 25 s jako heartbeat.
  Bez wiadomości przez 70 s strona łączy się ponownie, a co 5 min odpytuje `/api/frames/<id>/screen`.
- Po wdrożeniu nowej wersji strona przeładowuje się sama. Codziennie o 4:00 przeładowuje się też dla porządku w pamięci.
- Odjazdy pochodzą z nieudokumentowanego API strony MZK (`rj.mzk.zgora.pl`). Router trzyma odpowiedź 15 s w pamięci.
- Ostatni wysłany `screens.json` leży w `/app/data` i przeżywa restart kontenera.

## Wdrożenie

Push na `master` (poza `screens.json` i dokumentacją) buduje obraz `ghcr.io/wiktorkowalski/frame-router`
na runnerze homelab i restartuje kontener w `/home/ubuntu/docker/frame-router`.
Workflowy nie reagują na pull requesty: runner jest self-hosted, a repo jest publiczne.

Lokalnie:

```sh
cd router && dotnet test
FrameRouter__ConfigToken=dev dotnet run --project src/FrameRouter
```

Strona: <http://localhost:5000/?frame=dxmart> (port podaje `dotnet run`).

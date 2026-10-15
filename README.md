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
Opcjonalne `label` (np. `"Kuchnia"`) to nazwa ramki na stronie sterowania. Bez `label` strona pokazuje id ramki.

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

## Sterowanie ze strony

<https://frame.vicio.ovh/control.html> (z telefonu, z każdej sieci). Strona pokazuje wszystkie ramki z `screens.json`.

1. Przy pierwszym wejściu wklej token sterowania. Strona trzyma go w pamięci przeglądarki telefonu.
2. Wybierz czas („Pokaż przez”, domyślnie 30 min).
3. Stuknij ekran przy ramce. Ramka przełącza się w kilka sekund.
4. „Wróć do harmonogramu” kończy nadpisanie od razu.

Zasady nadpisania:

- Nadpisanie kończy się samo po wybranym czasie (1 min do 24 h). Ramka wraca do `schedule` i `default` z `screens.json`.
  Koniec dociera do ramki z opóźnieniem do 15 s.
- Push `screens.json` nie kasuje nadpisania. Gdy push usunie ekran nadpisania, ramka wraca do harmonogramu.
- Bez `minutes` w API nadpisanie trwa 30 min.
- Przyciemnianie (`dim`) działa dalej według `screens.json`.
- Nadpisania leżą w `/app/data/overrides.json` i przeżywają restart kontenera. Nic nie trafia do repo.

Token sterowania to `FrameRouter__ControlToken` w `.env` na hoście. Odczyt:

```sh
ssh ubuntu grep ControlToken /home/ubuntu/docker/frame-router/.env
```

Token może tylko nadpisywać ekrany. Nie zmienia `screens.json`. Bez tokena w `.env` sterowanie jest wyłączone (403).

### API sterowania

Każde wywołanie potrzebuje nagłówka `Authorization: Bearer <token sterowania>`.

| Metoda i ścieżka | Ciało | Działanie |
|---|---|---|
| `GET /api/control/frames` | — | Ramki, ich ekran i koniec nadpisania, lista ekranów |
| `PUT /api/control/frames/<id>/override` | `{"screen": "departures", "minutes": 30}` | Pokazuje ekran przez podany czas |
| `DELETE /api/control/frames/<id>/override` | — | Wraca do harmonogramu |

Przykład dla Home Assistant (`configuration.yaml`, token w `secrets.yaml` jako `frame_control_auth: "Bearer <token>"`):

```yaml
rest_command:
  frame_show:
    url: "https://frame.vicio.ovh/api/control/frames/{{ frame }}/override"
    method: put
    headers:
      Authorization: !secret frame_control_auth
    content_type: "application/json"
    payload: '{"screen": "{{ screen }}", "minutes": {{ minutes | default(30) }}}'
  frame_clear:
    url: "https://frame.vicio.ovh/api/control/frames/{{ frame }}/override"
    method: delete
    headers:
      Authorization: !secret frame_control_auth
```

Wywołanie: akcja `rest_command.frame_show` z danymi `frame: dxmart`, `screen: clock`, `minutes: 20`.
Ramka wraca do harmonogramu po akcji `rest_command.frame_clear` z danymi `frame: dxmart`.
Pierwszy blok `rest_command` w HA wymaga pełnego restartu. Kolejne komendy wystarczy przeładować w YAML.

Przycisk na dashboardzie (karta `button`):

```yaml
type: button
name: Zegar
icon: mdi:clock-outline
tap_action:
  action: perform-action
  perform_action: rest_command.frame_show
  data:
    frame: dxmart
    screen: clock
    minutes: 30
```

Na domowym HA te przyciski są na dashboardzie „Ramka” (`/dashboard-ramka`): zegar, ekran HA i powrót do harmonogramu.

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
- Kolejność dla ekranu: aktywne nadpisanie ze strony sterowania, potem pierwsza pasująca reguła `schedule`, potem `default`.

## Wdrożenie

Push na `master` (poza `screens.json` i dokumentacją) buduje obraz `ghcr.io/wiktorkowalski/frame-router`
na runnerze homelab i restartuje kontener w `/home/ubuntu/docker/frame-router`.
Workflowy nie reagują na pull requesty: runner jest self-hosted, a repo jest publiczne.

Lokalnie:

```sh
cd router && dotnet test
FrameRouter__ConfigToken=dev FrameRouter__ControlToken=dev dotnet run --project src/FrameRouter
```

Strona: <http://localhost:5000/?frame=dxmart> (port podaje `dotnet run`).

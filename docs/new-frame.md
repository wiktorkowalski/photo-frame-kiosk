# Nowa ramka: przygotowanie krok po kroku

Ta instrukcja opisuje drogę sprawdzoną 15.10.2026 na ramce Dxmart ZN-DP1002 (SSA RK3326, Android 8.1, WebView 106).
Inne modele mogą się różnić. Opisy innych ramek są w repo
[wesbos/photo-frame-dashboard](https://github.com/wesbos/photo-frame-dashboard) (`docs/devices.md`).

Cel: ramka po starcie pokazuje stronę `https://frame.home.vicio.ovh/?frame=<id>`.
O treści decyduje `screens.json` w tym repo. Frameo i aktualizatory producenta są wyłączone.

## Zasady

- Każdą zmianę na ramce zapisuj w `frames/<id>/changes.jsonl`: komenda, komenda cofająca, wynik sprawdzenia.
  Folder `frames/` jest w `.gitignore` i nie trafia do publicznego repo.
- Nie wpisuj do repo numerów seryjnych, adresów MAC ani kluczy.
- Po każdej komendzie sprawdź wynik, zanim przejdziesz dalej.

## Czego potrzebujesz

- Mac z `adb` (`brew install android-platform-tools`).
- Kabel USB-A → USB-C. Na Dxmart kabel C-C nie daje zasilania ani danych.
- APK Home HQ (`com.wesbos.homehq`), zbudowany z `dashboard/kiosk` w repo upstream (`build.sh`).
  Kod upstream nie ma licencji, więc APK i jego źródło nie leżą w tym repo.
  Podpisuj każdą ramkę **tym samym** keystore. Inaczej `adb install -r` z aktualizacją się nie uda.
- APK Rootless Pixel Launcher z F-Droid (zapasowy launcher).

## 1. Włącz ADB

1. Na ramce: Frameo → Settings → About → Beta program → **ADB access** = on.
2. Podłącz kabel. Na ramce zatwierdź klucz Maca.
3. Sprawdź połączenie i zapisz numer seryjny do zmiennej:

```sh
adb devices
export ADB_SERIAL=<numer z listy>
```

## 2. Sprawdź tożsamość (tylko odczyt)

```sh
adb -s "$ADB_SERIAL" shell getprop ro.build.fingerprint
adb -s "$ADB_SERIAL" shell wm size
adb -s "$ADB_SERIAL" shell wm density
adb -s "$ADB_SERIAL" shell dumpsys package com.android.webview | grep versionName
adb -s "$ADB_SERIAL" shell ls -l /system/xbin/su
```

- Strona ramki wymaga WebView 84 lub nowszego. Starsze ramki (na przykład WebView 61) wymagają innej drogi.
- Gęstość ma wynosić 160. Inna gęstość obcina dół strony: `adb shell wm density 160`.
- Bez `/system/xbin/su` kroki z `su 0` nie zadziałają.

Zapisz wyniki w `frames/<id>/SETUP.md`.

## 3. Zrób kopię zapasową

Zrób pełną kopię pamięci NAND, zanim cokolwiek zmienisz. Sposób zależy od układu.
Dla RK3326 wzór jest w `frames/dxmart-frame/` (`nand_backup.py`, `recovery.json`).
Kopię przenieś z Maca na NAS.

## 4. Wyłącz aktualizatory

```sh
adb -s "$ADB_SERIAL" shell pm list packages | grep -i -E 'fota|update|ota'
adb -s "$ADB_SERIAL" shell pm disable-user --user 0 com.adups.fota
adb -s "$ADB_SERIAL" shell pm disable-user --user 0 android.rockchip.update.service
```

Cofnięcie: `adb shell pm default-state --user 0 <pakiet>`.
Inny model może mieć inne pakiety. Wyłącz tylko te, które znalazł `pm list packages`.

## 5. Zainstaluj launcher zapasowy i kiosk

```sh
adb -s "$ADB_SERIAL" install rootless-launcher.apk
adb -s "$ADB_SERIAL" install homehq.apk
```

## 6. Ustaw kiosk jako ekran domowy

```sh
adb -s "$ADB_SERIAL" shell am start -n com.wesbos.homehq/.MainActivity --ez home true
adb -s "$ADB_SERIAL" shell cmd package set-home-activity --user 0 com.wesbos.homehq/.Home
```

Na ramce włącz Settings → Accessibility → **Home HQ navigation**. Ta usługa daje pływające przyciski
Back i Dashboard poza kioskiem. W kiosku przytrzymaj dwa nieruchome palce przez 1,5 s, a otworzy się menu.

## 7. Dodaj ramkę do `screens.json`

1. Wybierz id ramki: małe litery, cyfry i `-`, na przykład `kuchnia`.
2. Dodaj wpis w `frames`:

```json
"kuchnia": { "default": "clock" }
```

3. Zrób commit i push na `master`. Workflow **Push screens** wyśle plik do routera.
   Czerwony przebieg oznacza błąd w pliku. Treść błędu jest w logu przebiegu.

## 8. Skieruj kiosk na router

```sh
scripts/set-kiosk-url.sh kuchnia
```

Ramka musi rozwiązać `frame.home.vicio.ovh` na `192.168.1.12` i dosięgnąć tego adresu na porcie 443.
W osobnym VLAN-ie sprawdź regułę zapory.

## 9. Wyłącz Frameo

Android zapisuje stan pakietu na dysk z opóźnieniem. Natychmiastowy `adb reboot` gubi zmianę.

```sh
adb -s "$ADB_SERIAL" shell pm disable-user --user 0 net.frameo.frame
sleep 30
adb -s "$ADB_SERIAL" shell su 0 grep 'net.frameo.frame' /data/system/users/0/package-restrictions.xml
```

W wyniku musi być `enabled="3"`. Dopiero wtedy:

```sh
adb -s "$ADB_SERIAL" shell su 0 sync
adb -s "$ADB_SERIAL" reboot
```

Cofnięcie: `adb shell pm default-state --user 0 net.frameo.frame`.
`svc power reboot` na tej ramce nic nie robi. Używaj `adb reboot`.

## 10. Sprawdź po restarcie

```sh
adb -s "$ADB_SERIAL" shell dumpsys activity activities | grep -m1 mResumedActivity
adb -s "$ADB_SERIAL" shell pm list packages -d
adb -s "$ADB_SERIAL" shell settings get secure enabled_accessibility_services
```

- Na wierzchu jest `com.wesbos.homehq`.
- Frameo i aktualizatory są na liście wyłączonych pakietów.
- Usługa `com.wesbos.homehq/.NavigationService` jest włączona.
- Ekran pokazuje ekran z `screens.json`. Zmiana `default` w repo przełącza ramkę w około 30 s.

## Znane problemy

- Przy uśpionym ekranie Wi-Fi nie łączy się ponownie (sterownik nie obsługuje skanowania w tle).
  Kiosk trzyma ekran włączony. Do przyciemniania użyj strony, nie usypiania.
- Na PF1007L aktualizacja kiosku (`adb install -r`) wyłączała usługę nawigacji. Na Dxmart tego jeszcze nie sprawdzono.
  Po aktualizacji kiosku sprawdź krok 10.

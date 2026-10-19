# Zagrody

Program do porządkowania pulpitu w Windows 10/11, w stylu Stardock Fences, z kilkoma rzeczami, których tam brakuje.

## Co potrafi

**Zagrody na pulpicie**
- Półprzezroczyste panele z rozmyciem tła. Leżą pod oknami, nie ma ich w Alt+Tab ani na pasku zadań i nie znikają po Win+D.
- Dwa rodzaje zagród:
  - **zwykła**: przypinasz do niej pliki, foldery i skróty, a one zostają na swoim miejscu na dysku;
  - **folderowa**: pokazuje na żywo zawartość wybranego folderu (np. Pobrane albo `D:\Gry`).
- Przeciąganie plików z Eksploratora, między zagrodami i z zagród do innych programów. Ctrl wymusza kopiowanie, Shift przenoszenie.
- Zwijanie do paska tytułu (dwuklik w pasek). Zwinięta zagroda rozwija się po najechaniu myszą.
- Ręczne układanie ikon przeciąganiem albo sortowanie według nazwy, typu, daty lub rozmiaru.
- Kolor, przezroczystość tła, rozmiar ikon (do 96 px, z ostrymi ikonami „jumbo”), blokada położenia, przyciąganie do siatki.
- Dwuklik na pustym pulpicie chowa zagrody razem z ikonami pulpitu. Kolejny dwuklik je przywraca.
- Przypięte elementy, które zniknęły z dysku, są wyszarzone. Jedną komendą usuwasz wszystkie brakujące.

**Wyszukiwanie** (rzeczy, których Fences nie ma)
- Pisanie w zagrodzie od razu ją filtruje.
- **Ctrl+Alt+Spacja** otwiera wyszukiwarkę po wszystkich zagrodach naraz. Rozumie skróty („wrd” znajdzie „Word”) i ignoruje polskie znaki („zolw” znajdzie „Żółw”).
  Enter otwiera, Ctrl+Enter pokazuje plik w Eksploratorze, Alt+Enter pokazuje go w zagrodzie.

**Automatyczne sortowanie**
- Reguły według rozszerzenia, wzorca nazwy (`faktura*;*rachunek*`), wieku i rozmiaru, do tego gotowe zestawy (Obrazy, Dokumenty, Archiwa, Instalatory…).
- **Podgląd** pokazuje, co by się stało, zanim cokolwiek zostanie przeniesione.
- **Cofanie** ostatnich sortowań z menu w zasobniku.
- Pliki w trakcie pobierania (`.crdownload`, `.part`…), otwarte, ukryte i systemowe są pomijane.
- Folder podpięty pod inną zagrodę nigdy nie zostanie przeniesiony, a przypięte skróty idą za przenoszonymi plikami.

**Pewność danych**
- Ustawienia zapisują się atomowo, z kopią zapasową. Uszkodzony plik jest odkładany na bok i program odtwarza ustawienia z kopii.
- Zagroda, która znalazła się poza ekranem (np. po odłączeniu monitora), wraca na ekran główny.

## Pobranie i uruchomienie

1. Wejdź w zakładkę **Actions** repozytorium, otwórz ostatni udany przebieg „Build” i pobierz artefakt **Zagrody-win-x64**.
2. Rozpakuj go i uruchom `Zagrody.exe`. To jeden plik, nie wymaga instalacji ani .NET.
3. Ikona programu pojawi się w zasobniku obok zegara. Prawy przycisk na niej otwiera menu.

Autostart włączysz w *Ustawienia → Ogólne*. Ustawienia są w `%APPDATA%\Zagrody\config.json`.

## Skróty

| Skrót | Działanie |
|---|---|
| Ctrl+Alt+Spacja | wyszukiwarka we wszystkich zagrodach |
| Ctrl+Alt+H | pokaż / ukryj zagrody |
| dwuklik na pulpicie | pokaż / ukryj zagrody i ikony pulpitu |
| dwuklik na pasku zagrody | zwiń / rozwiń |
| F2 | zmień nazwę zagrody |
| Ctrl+F lub zacznij pisać | filtruj zagrodę |
| Enter / Delete | otwórz / usuń z zagrody (w folderowej: do kosza) |

Oba skróty globalne można zmienić w ustawieniach.

## Budowanie ze źródeł

Wymagany jest .NET 8 SDK.

```
dotnet test tests/Zagrody.Core.Tests
dotnet run --project src/Zagrody.App
```

Struktura:

- `src/Zagrody.Core`: logika niezależna od interfejsu (model, zapis ustawień, reguły, planowanie i cofanie sortowania, wyszukiwanie, geometria). Działa na każdym systemie i ma testy.
- `src/Zagrody.App`: aplikacja WPF (okna zagród, integracja z pulpitem przez Win32, zasobnik, ustawienia).
- `tests/Zagrody.Core.Tests`: testy xUnit.

<div align="center">

<img src="https://raw.githubusercontent.com/GrzybDev/JustModIt-Manager/main/src/MenadzerSpolszczen/Branding/logo-256.png" alt="" width="96" height="96">

# Menadżer Spolszczeń

Aplikacja [Bazy Spolszczeń](https://bazaspolszczen.pl) na komputer: znajduje Twoje gry, instaluje do nich spolszczenia
tworzone przez fanów, dba o ich aktualność i potrafi cofnąć każdą zmianę.

[![Najnowsze wydanie](https://img.shields.io/github/v/release/GrzybDev/MenadzerSpolszczen?label=wydanie)](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)
[![Pobrania](https://img.shields.io/github/downloads/GrzybDev/MenadzerSpolszczen/total?label=pobrania)](https://github.com/GrzybDev/MenadzerSpolszczen/releases)
![Windows 10/11 i Linux, x86-64](https://img.shields.io/badge/Windows%2010%2F11%20%7C%20Linux-x86--64-0078D4)

**[Pobierz](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)** ·
[Baza Spolszczeń](https://bazaspolszczen.pl) ·
[Zgłoś problem](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose) ·
[Kod źródłowy](https://github.com/GrzybDev/JustModIt-Manager)

*In English: [README.md](README.md)*

</div>

To repozytorium służy tylko do publikowania wydań aplikacji i zbierania zgłoszeń graczy. Jej kod źródłowy jest w
repozytorium [JustModIt Manager](https://github.com/GrzybDev/JustModIt-Manager).

## Pobieranie

### Windows

Instalator `MenadzerSpolszczen-win-Setup.exe` jest w [najnowszym
wydaniu](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest). Aplikacja instaluje się dla bieżącego
użytkownika Windows, bez uprawnień administratora, a potem sama się aktualizuje.

- Wymaga Windows 10 lub 11 w wersji 64-bitowej.
- Aplikacja i jej instalator są podpisane cyfrowo przez „Open Source Developer, Marek Grzyb”.

### Linux i Steam Deck

Aplikacja wymaga 64-bitowego Linuksa na procesorze Intel lub AMD, z systemd i glibc w wersji 2.35 lub nowszej, na
przykład: SteamOS od wersji 3.7 na Steam Decku, Bazzite, Fedora, Ubuntu od 22.04, Arch Linux, Linux Mint. Instaluje się
w folderze domowym, bez hasła administratora, a potem sama się aktualizuje.

1. Na Steam Decku najpierw przełącz się na tryb pulpitu: przycisk STEAM › Zasilanie › Przełącz na pulpit.
2. Otwórz terminal (Konsole w KDE i na Steam Decku, Terminal w GNOME), wklej to polecenie i naciśnij Enter:

   ```sh
   curl -fsSL https://bazaspolszczen.pl/linux | sh
   ```

Polecenie pobiera aplikację do `~/Applications/MenadzerSpolszczen.AppImage`, porównuje ją z sumą kontrolną z wydania i
ją uruchamia. Gdy coś się nie uda, terminal pokazuje opis błędu, a to samo polecenie można uruchomić jeszcze raz.

Zamiast polecenia można pobrać z [najnowszego wydania](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)
plik `menadzer-spolszczen-install.desktop`, który uruchamia to samo polecenie, i otworzyć go w menedżerze plików; KDE
najpierw zapyta, czy uruchomić plik z internetu, co trzeba potwierdzić. Firefox zmienia nazwy pobranych plików
`.desktop`, więc w Firefoksie trzeba pobrać `menadzer-spolszczen-install.zip` i otworzyć plik z tego archiwum. GNOME nie
uruchamia pobranych plików `.desktop`, więc tam zostaje polecenie.

## Co potrafi

- **Znajduje gry** ze Steama, GOG-a (GOG Galaxy, instalatory offline i Heroic) i Epic Games Store (Epic Games Launcher,
  Heroic i Legendary) i na bieżąco śledzi zmiany w sklepach, bez przeszukiwania dysków.
- **„Moje gry”** pokazują gry, do których Baza Spolszczeń ma ukończone spolszczenie pasujące do zainstalowanej wersji
  gry, a **„Katalog”** pozwala przejrzeć wszystkie spolszczenia ze strony.
- **Instaluje spolszczenia** jednym kliknięciem albo automatycznie, najpierw sprawdza, czy spolszczenie pasuje do
  zainstalowanej wersji gry, i nigdy nie rusza gry, która jest uruchomiona albo właśnie aktualizowana przez sklep.
- **Otwiera się ze strony**: „Pobierz” na stronie spolszczenia w Bazie Spolszczeń może otworzyć je od razu w aplikacji,
  z częściami wybranymi na stronie. Nic nie instaluje się, dopóki nie klikniesz „Zainstaluj”.
- **Pozwala wybrać spolszczenie**, gdy tę samą grę przetłumaczyło kilka grup, i jego części do wyboru; o instalacjach
  automatycznych decydują grupy wskazane w ustawieniach jako preferowane.
- **Dba o aktualność**: instaluje nowe wydania zainstalowanych spolszczeń i przywraca spolszczenie, gdy aktualizacja gry
  je nadpisze. Do gry, która oficjalnie ma język polski, spolszczenie nigdy nie jest instalowane automatycznie.
- **Pozwala cofnąć wszystko**, także bez internetu: zachowuje oryginalne pliki (domyślnie w ukrytym folderze
  `.menadzer-spolszczen` w folderze gry), a instalację przerwaną przez awarię lub brak prądu sama wycofuje.
- **Tłumaczy osiągnięcia w Steamie**, gdy grupa je przetłumaczyła: Steam pokazuje polskie teksty w bibliotece, nakładce
  w grze i powiadomieniach, jeśli jego interfejs jest po polsku, a włącza się je i wyłącza na stronie gry. Gry, które
  wyświetlają osiągnięcia we własnych menu, i strony Steama w przeglądarce zostają bez zmian.
- **Działa po cichu** w tle, z ikoną w zasobniku systemowym, jeśli pulpit go ma, i uruchamia się razem z systemem; tę
  opcję można wyłączyć.
- **Kreator spolszczeń**, dostępny w trybie twórcy, zapisuje projekty, rozpoznaje grę po przetłumaczonych plikach w
  folderze i z jednego projektu buduje paczki dla kilku wersji gry oraz jej składników.

## Pytania

**Mojej gry nie ma w „Moich grach”.** Lista pokazuje tylko gry, do których jest ukończone spolszczenie pasujące do
Twojej wersji gry. Jeśli gra jest zainstalowana, a aplikacja jej nie znalazła, otwórz „Nie widzisz swojej gry?” w
„Moich grach”: pokazuje, co zgłosił każdy sklep, i pozwala wskazać folder biblioteki, z którego korzysta sklep.

**Windows ostrzega przed instalatorem.** Instalator jest podpisany, ale Windows SmartScreen może ostrzegać przed nowymi
wydaniami, dopóki nie zostaną pobrane odpowiednio wiele razy. Sprawdź, czy ostrzeżenie podaje jako wydawcę „Open Source
Developer, Marek Grzyb”, a potem wybierz „Więcej informacji” i „Uruchom mimo to”.

**Czy potrzebuję konta w Bazie Spolszczeń?** Nie. Aplikacja czyta publiczny katalog i nie wymaga logowania.

**Jak działa na Steam Decku?** Instaluje się w trybie pulpitu, jak opisano [wyżej](#linux-i-steam-deck). Tryb gry
pokazuje tylko to, co uruchamia Steam, więc aplikację trzeba dodać do biblioteki Steama: proponuje to przy pierwszym
uruchomieniu, a później służy do tego „Skrót w bibliotece Steama” w ustawieniach. W trybie gry aplikacja otwiera się
wtedy z biblioteki jak gra, a jej część działająca w tle instaluje i aktualizuje spolszczenia w obu trybach. Tryb gry
nie ma zasobnika ani powiadomień, więc to, co czeka na decyzję, jest w panelu „Do zrobienia” w „Moich grach”.
Tłumaczenia osiągnięć trafiają do Steama przez tryb klasyczny („Tryb klasyczny bez Millennium” w ustawieniach), bo na
SteamOS nie da się zainstalować Millennium.

**Czy działa na macOS?** Nie. Działa na Windows i na Linuksie z procesorem Intel lub AMD, nie na ARM.

## Prywatność

Aplikacja nie zbiera danych o sposobie korzystania z niej. Łączy się tylko z Bazą Spolszczeń (lista spolszczeń, paczki
i obrazy), z GitHubem (aktualizacje aplikacji) i z serwerami obrazów pokazywanych przez stronę. Pobranie paczki liczy
się na stronie spolszczenia tak samo jak pobranie ze strony. Żeby liczyło się raz na instalację, tylko to żądanie niesie
losowy identyfikator instalacji, który nic nie mówi o graczu ani o komputerze; ponowna instalacja aplikacji daje nowy.

Po nieoczekiwanym zamknięciu aplikacja może wysłać opis błędu jej autorom: tylko za zgodą albo bez pytania, gdy
włączy się to w ustawieniach. Przed wysłaniem widać, co pójdzie; raport zawiera tylko sam błąd, bez nazwy użytkownika,
zawartości plików, ścieżek i gier. Szczegóły opisuje
[polityka prywatności Bazy Spolszczeń](https://bazaspolszczen.pl/legal/privacy-policy).

## Zgłaszanie problemów

- **Problem z aplikacją albo pomysł:** [utwórz zgłoszenie](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose),
  po polsku albo po angielsku. W aplikacji „O programie” w ustawieniach otwiera je z wpisanymi wersjami.
- **Problem ze spolszczeniem** (złe pliki, wadliwe wydanie): strona spolszczenia w Bazie Spolszczeń albo jej
  [formularz kontaktowy](https://bazaspolszczen.pl/contact).
- **Problem z bezpieczeństwem:** zgłoś go prywatnie, jak opisuje
  [zasada bezpieczeństwa](https://github.com/GrzybDev/JustModIt-Manager/security/policy).

## Odinstalowywanie

Zainstalowane spolszczenia zostają w grach: aby odzyskać oryginalne pliki, najpierw trzeba odinstalować spolszczenia w
aplikacji. Ustawienia i zapisy aplikacji też zostają, więc po ponownej instalacji wszystko wraca na swoje miejsce.

- **Windows:** aplikację odinstalowuje się w Ustawieniach Windows, w sekcji Aplikacje. Jej dane zostają w
  `%LOCALAPPDATA%\BazaSpolszczen\MenadzerSpolszczen`.
- **Linux:** aplikację odinstalowuje się w niej samej, w ustawieniach: „O programie” › „Odinstaluj aplikację”, albo
  akcją „Odinstaluj” jej wpisu w menu. Jej dane zostają w `~/.local/share/BazaSpolszczen/MenadzerSpolszczen`, a skrót
  w bibliotece Steama zostaje, dopóki nie usunie się go w Steamie.

## Licencja

Kod aplikacji jest wolnym oprogramowaniem na licencji GNU General Public License w wersji 3 lub późniejszej. Nazwy i
logotypy Bazy Spolszczeń i Menadżera Spolszczeń nie są objęte tą licencją. Szczegóły, a także informację, gdzie znaleźć
kod źródłowy każdego wydania, zawiera [LICENSE.md](LICENSE.md).

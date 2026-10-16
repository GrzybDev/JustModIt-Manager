<div align="center">

<img src="https://raw.githubusercontent.com/GrzybDev/JustModIt-Manager/main/src/MenadzerSpolszczen/Branding/logo-256.png" alt="" width="96" height="96">

# Menadżer Spolszczeń

Aplikacja [Bazy Spolszczeń](https://bazaspolszczen.pl) na komputer: znajduje Twoje gry, instaluje do nich spolszczenia
tworzone przez fanów, dba o ich aktualność i potrafi cofnąć każdą zmianę.

[![Najnowsze wydanie](https://img.shields.io/github/v/release/GrzybDev/MenadzerSpolszczen?label=wydanie)](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)
[![Pobrania](https://img.shields.io/github/downloads/GrzybDev/MenadzerSpolszczen/total?label=pobrania)](https://github.com/GrzybDev/MenadzerSpolszczen/releases)
![Windows 10/11 x64](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D4)

**[Pobierz](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest)** ·
[Baza Spolszczeń](https://bazaspolszczen.pl) ·
[Zgłoś problem](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose) ·
[Kod źródłowy](https://github.com/GrzybDev/JustModIt-Manager)

*In English: [README.md](README.md)*

</div>

To repozytorium służy tylko do publikowania wydań aplikacji i zbierania zgłoszeń graczy. Jej kod źródłowy jest w
repozytorium [JustModIt Manager](https://github.com/GrzybDev/JustModIt-Manager).

## Pobieranie

Instalator `MenadzerSpolszczen-win-Setup.exe` jest w [najnowszym
wydaniu](https://github.com/GrzybDev/MenadzerSpolszczen/releases/latest). Aplikacja instaluje się dla bieżącego
użytkownika Windows, bez uprawnień administratora, a potem sama się aktualizuje.

- Wymaga Windows 10 lub 11 w wersji 64-bitowej.
- Aplikacja i jej instalator są podpisane cyfrowo przez „Open Source Developer, Marek Grzyb”.

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
- **Działa po cichu** w zasobniku systemowym i uruchamia się razem z Windows; tę opcję można wyłączyć.
- **Kreator spolszczeń**, dostępny dla tłumaczy w trybie twórcy, buduje paczki ze spolszczeniem z folderu z
  przetłumaczonymi plikami.

## Pytania

**Mojej gry nie ma w „Moich grach”.** Lista pokazuje tylko gry, do których jest ukończone spolszczenie pasujące do
Twojej wersji gry. Jeśli gra jest zainstalowana, a aplikacja jej nie znalazła, otwórz „Nie widzisz swojej gry?” w
„Moich grach”: pokazuje, co zgłosił każdy sklep, i pozwala wskazać folder biblioteki, z którego korzysta sklep.

**Windows ostrzega przed instalatorem.** Instalator jest podpisany, ale Windows SmartScreen może ostrzegać przed nowymi
wydaniami, dopóki nie zostaną pobrane odpowiednio wiele razy. Sprawdź, czy ostrzeżenie podaje jako wydawcę „Open Source
Developer, Marek Grzyb”, a potem wybierz „Więcej informacji” i „Uruchom mimo to”.

**Czy potrzebuję konta w Bazie Spolszczeń?** Nie. Aplikacja czyta publiczny katalog i nie wymaga logowania.

**Czy działa na Linuksie albo macOS?** Jeszcze nie. Aplikacja jest zbudowana tak, żeby mogły do niej dołączyć inne
systemy.

## Prywatność

Aplikacja nie zbiera danych o sposobie korzystania z niej. Łączy się tylko z Bazą Spolszczeń (lista spolszczeń, paczki
i obrazy), z GitHubem (aktualizacje aplikacji) i z serwerami obrazów pokazywanych przez stronę. Pobranie paczki liczy
się na stronie spolszczenia tak samo jak pobranie ze strony. Żeby liczyło się raz na instalację, tylko to żądanie niesie
losowy identyfikator instalacji, który nic nie mówi o graczu ani o komputerze; ponowna instalacja aplikacji daje nowy.

Po nieoczekiwanym zamknięciu aplikacja może wysłać opis błędu jej autorom: tylko za zgodą albo bez pytania, gdy
włączy się to w ustawieniach. Przed wysłaniem widać, co pójdzie; raport nie zawiera nazwy użytkownika ani zawartości
plików, choć ostatnie wpisy dziennika mogą wymieniać gry i foldery, których dotyczyły. Szczegóły opisuje
[polityka prywatności Bazy Spolszczeń](https://bazaspolszczen.pl/legal/privacy-policy).

## Zgłaszanie problemów

- **Problem z aplikacją albo pomysł:** [utwórz zgłoszenie](https://github.com/GrzybDev/MenadzerSpolszczen/issues/new/choose),
  po polsku albo po angielsku. W aplikacji „O programie” w ustawieniach otwiera je z wpisanymi wersjami.
- **Problem ze spolszczeniem** (złe pliki, wadliwe wydanie): strona spolszczenia w Bazie Spolszczeń albo jej
  [formularz kontaktowy](https://bazaspolszczen.pl/contact).
- **Problem z bezpieczeństwem:** zgłoś go prywatnie, jak opisuje
  [zasada bezpieczeństwa](https://github.com/GrzybDev/JustModIt-Manager/security/policy).

## Odinstalowywanie

Aplikację odinstalowuje się w Ustawieniach Windows, w sekcji Aplikacje. Zainstalowane spolszczenia zostają w grach:
aby odzyskać oryginalne pliki, najpierw trzeba odinstalować spolszczenia w aplikacji. Ustawienia i zapisy aplikacji
zostają w `%LOCALAPPDATA%\BazaSpolszczen\MenadzerSpolszczen`, więc po ponownej instalacji wszystko wraca na swoje
miejsce.

## Licencja

Kod aplikacji jest wolnym oprogramowaniem na licencji GNU General Public License w wersji 3 lub późniejszej. Nazwy i
logotypy Bazy Spolszczeń i Menadżera Spolszczeń nie są objęte tą licencją. Szczegóły, a także informację, gdzie znaleźć
kod źródłowy każdego wydania, zawiera [LICENSE.md](LICENSE.md).

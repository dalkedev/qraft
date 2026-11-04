# Qraft z symulatorem danych

Autorzy: Mateusz Dalke, 124382; Oskar Kirko, 123758. Politechnika Bydgoska, Informatyka Stosowana, III rok, V semestr, grupa 1.

## Cel i zakres

Qraft będzie kontrolował jakość regranulatu PP na etapach E1 i E2. Rozszerzenie dodaje osobny symulator, który przechowuje przykładowe dane we własnej bazie i przekazuje je kolejką do Qraft. Qraft sprawdza zawartość, wylicza ocenę, zapisuje wynik i przesyła odpowiedź. Symulator weryfikuje odpowiedź i zachowuje ją razem z danymi źródłowymi.

Aktualne środowisko developmentu działa na macOS: ASP.NET Core, usługi w tle, Vue, RabbitMQ i dwie bazy SQLite. Logika domenowa nie zależy od interfejsu. Pierwotny dokument zakładał WPF; wybór formy interfejsu na zaliczenie wymaga ustalenia z prowadzącym.

## Wymagania rozszerzenia

| ID | Wymaganie |
| --- | --- |
| S01 | Symulator ma własne trwałe scenariusze i dane źródłowe, niezależne od bazy Qraft. |
| S02 | Utworzenie symulacji zapisuje dane oraz zadania wysyłki. Wysyłanie odbywa się przez kolejkę. |
| S03 | Qraft weryfikuje JSON, wersję schematu, identyfikatory, kompletność i wartości pomiarów. |
| S04 | Poprawny pomiar poza limitem oznacza niezgodność jakości, a błąd kontraktu odrzucenie komunikatu. |
| S05 | Qraft zapisuje wejście, wynik i zadanie odpowiedzi trwale. Odpowiedź wraca kolejką. |
| S06 | Symulator sprawdza korelację i spójność wyniku oraz zapisuje odpowiedź we własnej bazie. |
| S07 | Ponowne dostarczenie tej samej wiadomości nie duplikuje zapisanej oceny. Przerwa brokera nie usuwa outboxa. |
| S08 | Panel pozwala uruchomić scenariusz, zobaczyć wynik i szczegóły walidacji oraz ponowić wysyłkę. |

## Kryteria dydaktyczne

Każde badanie zawiera trzy próbki. Wilgotność ma mieścić się w zakresie 0–0,200%, gęstość 880–950 kg/m³, a barwa ma być zgodna ze wzorcem. Granice są włączone. Próbka jest zgodna, gdy wszystkie kryteria są spełnione; badanie, gdy wszystkie próbki są zgodne. Limity są przykładowymi danymi dydaktycznymi.

## Rozdzielenie etapów implementacji

W szkielecie działa odbiór, walidacja, ocena i dwustronny transport z zapisem. Procesor akceptuje oznaczenie E1/E2, lecz nie egzekwuje jeszcze całego cyklu produkcyjnego. Akceptacja E1, przygotowanie do E2, decyzje o poprawie i zwolnienie partii pozostają kolejnymi zadaniami domenowymi. Wynik automatycznej oceny nie zastępuje decyzji o zwolnieniu.

## Stan weryfikacji

Pełny build rozwiązania, 40 testów C# i 10 testów Vue zakończyły się pomyślnie. Typecheck i build Vue są poprawne. Aktualne wyniki i granice potwierdzenia: [WERYFIKACJA.md](WERYFIKACJA.md).

## Odbiór rozszerzenia

Scenariusze obejmują zgodne i niezgodne pomiary, wartości graniczne, brak wymaganych danych oraz uszkodzony JSON. Dla każdego komunikatu identyfikatory w wyniku i obu bazach muszą się zgadzać. Powtórzona wiadomość ma zachować ResultId i liczbę zapisów. Po przerwie brokera oczekujące zadanie musi zostać wysłane i otrzymać wynik.

Szczegóły komponentów i kontraktów: [ARCHITEKTURA.md](development/ARCHITEKTURA.md). Kolejne zadania implementacyjne: [START.md](development/START.md).

# Qraft

Środowisko developmentu: C#/.NET 10, ASP.NET Core, Vue 3, RabbitMQ i dwie niezależne bazy SQLite. Interfejs developmentu jest webowy; WPF nie jest częścią obecnych źródeł.

## Stan implementacji

Działa pionowy przekrój odbioru, walidacji, oceny i zapisu wyników. Pełny build oraz 40 testów C# i 10 testów Vue zaliczono na przypiętych wersjach narzędzi; typecheck i build Vue są poprawne. Aktualne wyniki integracji: [WERYFIKACJA.md](docs/WERYFIKACJA.md). Ręczne decyzje E1/E2 i zwalnianie partii pozostają planowane.

## Uruchomienie

Wymagane: SDK .NET 10.0.301, Node.js 22.22.3 oraz uruchomiony Docker z Compose (na macOS: Docker Desktop). Wersje SDK, Node i zależności są zapisane w repozytorium.

```sh
npm run setup
npm run dev
```

Setup instaluje lokalne narzędzia i zależności oraz buduje rozwiązanie. Dev uruchamia RabbitMQ, API, symulator, worker i Vue. Zmiany C# obsługuje dotnet watch, a Vue — Vite. Jeśli korzystasz z fnm: `fnm use 22.22.3`.

| Usługa | Adres |
| --- | --- |
| Panel Qraft Lab | http://127.0.0.1:5173 |
| Qraft API / OpenAPI | http://127.0.0.1:5280/openapi/v1.json |
| Symulator / OpenAPI | http://127.0.0.1:5281/openapi/v1.json |
| RabbitMQ Management | http://127.0.0.1:15677 |

Lokalne konto brokera: `qraft` / `qraft-local-dev`, vhost `/qraft`. Port AMQP: `5677`. Usługi są dostępne tylko z lokalnego komputera. Ustawienia można nadpisać zmiennymi z `.env.example`; pliku `.env` nie zapisujemy w Git.

Zatrzymanie: Ctrl+C w terminalu launchera albo `npm run stop`. Procesy i broker są zatrzymywane; pliki baz oraz wolumen RabbitMQ zostają. Logi: `.dev/logs/`.

## Co już działa

1. Symulator przechowuje pięć scenariuszy w `data/simulator.db` i tworzy z nich komunikaty.
2. Zapisuje źródło oraz zadanie wysyłki w jednej transakcji, a następnie publikuje je do RabbitMQ.
3. Qraft Worker sprawdza JSON, schemat, identyfikatory, kompletność i wartości. Poprawne pomiary ocenia według kryteriów wilgotności, gęstości i barwy.
4. Zapisuje komunikat, ocenę próbek i zadanie odpowiedzi w `data/qraft.db`.
5. Symulator odbiera odpowiedź, sprawdza korelację i spójność wyniku, zapisuje ją we własnej bazie. Vue pokazuje rzeczywiste dane obu usług.

`Rejected` oznacza wadliwy komunikat. `Processed / Nonconformant` oznacza poprawny komunikat opisujący niezgodny materiał. Ocena nie zwalnia automatycznie partii do produkcji.

## Gdzie pisać kod

| Katalog | Odpowiedzialność |
| --- | --- |
| `src/Qraft.Domain` | Kryteria i ocena jakości, bez zależności od transportu lub bazy |
| `src/Qraft.Application` | Walidacja wejścia, procesor i kontrakt IQualityStore |
| `src/Qraft.Infrastructure` | SQLite, migracje, zapis wyników i outbox odpowiedzi |
| `src/Qraft.Contracts` | Wersjonowane struktury wiadomości i nazwy kolejek |
| `src/Qraft.Messaging` | Połączenie RabbitMQ, potwierdzenia, ACK i DLQ |
| `src/Qraft.Api` | Odczyt wyników i endpointy panelu |
| `src/Qraft.Worker` | Odbiór, przetwarzanie i publikacja odpowiedzi |
| `src/Qraft.Simulator` | Własna baza danych źródłowych, generowanie, outbox i odbiór wyników |
| `apps/web` | Panel Vue + TypeScript |
| `tests/Qraft.UnitTests` | Testy reguł, walidacji i trwałości |

Zacznij od `src/Qraft.Domain/QualityEvaluation.cs`, potem `src/Qraft.Application/InspectionProcessor.cs`. Przepływ transportowy znajduje się w `src/Qraft.Worker/InspectionConsumer.cs`. Lista kolejnych zadań: [START.md](docs/development/START.md).

## Sprawdzenie

```sh
npm test                  # testy C#, testy parsera Vue, typecheck i build Vue
npm run test:smoke        # przy działającym dev: 5 scenariuszy i powtórzenie wiadomości
npm run test:resilience   # zatrzymuje tylko broker Qraft, sprawdza outbox i przywraca broker
```

`npm run dev:stable` uruchamia ten sam zestaw bez dotnet watch. Obie bazy mają migracje EF Core. Zmianę modelu zapisuj nową migracją; nie kasuj plików SQLite w celu obejścia błędu.

## Zakres na start

Gotowy jest pionowy przekrój odbioru i oceny danych. Pełny proces ręcznych decyzji E1/E2, przygotowania materiału, poprawy oraz zwalniania partii pozostaje do implementacji. Samo pole `stage` w wiadomości nie egzekwuje jeszcze kolejności etapów.

Aktualny opis rozszerzenia: [SPECYFIKACJA.md](docs/SPECYFIKACJA.md). Techniczny przepływ i zasady ponowień: [ARCHITEKTURA.md](docs/development/ARCHITEKTURA.md). Zaktualizowana dokumentacja uczelniana i źródła rysunków znajdują się w [docs/academic](docs/academic/): [Word](docs/academic/Qraft_specyfikacja.docx), [PDF](docs/academic/Qraft_specyfikacja.pdf). Dopuszczenie interfejsu webowego do zaliczenia pozostaje do ustalenia z prowadzącym.

Autorzy: Mateusz Dalke, 124382; Oskar Kirko, 123758. Politechnika Bydgoska, Informatyka Stosowana.

## CI

Przykładowy workflow znajduje się w [examples/github-actions/ci.yml](examples/github-actions/ci.yml). Nie jest aktywny w tej publikacji: użyte połączenie GitHub nie ma uprawnienia `workflow`. Nie ma potwierdzonego uruchomienia GitHub Actions. Po stronie właściciela pozostaje zapisanie workflow pod `.github/workflows/ci.yml` z odpowiednio uprawnionego połączenia. Lokalne wyniki nie są wynikiem CI.

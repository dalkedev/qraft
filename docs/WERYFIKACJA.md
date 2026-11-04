# Weryfikacja implementacji

Sprawdzenie wykonano 4 listopada 2026 na Linux x64, z SDK .NET 10.0.301 i Node.js 22.22.3. Użyto istniejących lockfile; nie aktualizowano zależności. Oryginalny model i migracje symulatora są częścią źródeł.

| Sprawdzenie | Rzeczywisty wynik |
| --- | --- |
| `dotnet tool restore` | Powodzenie |
| `dotnet restore Qraft.slnx --locked-mode` | Powodzenie |
| `npm ci --prefix apps/web` | Powodzenie |
| `dotnet build Qraft.slnx --no-restore -m:1` | Powodzenie, 0 błędów i 0 ostrzeżeń |
| `npm test` | 40 testów C#, 10 testów Vue, typecheck i build Vite zakończone pomyślnie |
| `npm run setup` | Zatrzymany na kontroli wymaganego Docker; kroki zależności i budowania wykonano osobno |
| Launcher `node scripts/dev.mjs --no-watch --skip-broker` | API, worker, symulator i Vue uruchomione; gotowość potwierdzona |
| `npm run test:smoke` | Pięć scenariuszy oraz replay zaliczone z rzeczywistym brokerem |
| `npm run test:resilience` | Zatrzymanie/powrót rzeczywistego brokera zaliczone przez opisany niżej adapter sterowania |
| GitHub Actions | Brak uruchomienia; workflow jest nieaktywnym przykładem |

## Rzeczywisty obieg i retransmisja

Test smoke wykonał `good` → `Processed / Conformant`, `nonconformant` → `Processed / Nonconformant`, `boundary` → `Processed / Conformant` oraz `invalid-payload` i `malformed-json` → `Rejected` bez oceny jakości. Skrypt porównał identyfikatory wiadomości, symulacji i wyniku w obu usługach, oceny trzech próbek oraz błędy odrzucenia. Replay zachował liczbę odebranych komunikatów Qraft i ten sam `ResultId` po obu stronach.

Test odporności zatrzymał broker, utworzył żądanie i sprawdził brak wyniku oraz oczekujące zadanie outbox. Po ponownym uruchomieniu potwierdził `Processed / Conformant`, trwały zapis w obu bazach i zgodny identyfikator wyniku. Test dotyczy zatrzymania i powrotu procesu; nie stanowi dowodu odporności na dowolną awarię sprzętu.

## Środowisko brokera i granica potwierdzenia

Uruchomiono izolowany **natywny RabbitMQ 4.2.0 z Erlang 27.3.4.6**, na lokalnych portach projektu. Nie było dostępnego Docker. Skrypt smoke nie wymagał zmian. Oryginalny skrypt resilience wywołuje Docker Compose; prywatny, ograniczony adapter przełożył wyłącznie jego polecenia zatrzymania/uruchomienia tego brokera na sterowanie rzeczywistym procesem natywnym. Kolejki, komunikaty i bazy nie były symulowane, kod aplikacji i skryptów pozostał bez zmian.

Pierwsza próba powrotu nie zakończyła się z powodu konfiguracji izolowanego runtime’u brokera. Po korekcie jego sposobu odczytu pamięci przywrócono ten sam węzeł i dane; najpierw potwierdzono zakończenie już oczekującego przebiegu, następnie powtórzono cały test z wynikiem pozytywnym. Nie kasowano baz, kolejki ani stanu brokera.

**Nie potwierdzono wykonania konkretnego obrazu Docker przypiętego w Compose ani pełnego `npm run setup` z Dockerem.** Standardowa instrukcja uruchomienia nadal wymaga Docker/Compose. Test natywny potwierdza opisany obieg aplikacji i zachowanie przy przerwie brokera.

## CI i zakończenie

Połączenie użyte do publikacji nie posiada uprawnienia GitHub `workflow`. Workflow zachowano w [examples/github-actions/ci.yml](../examples/github-actions/ci.yml); nie jest aktywny. Aktywacja wymaga umieszczenia go w `.github/workflows/ci.yml` przez odpowiednio uprawnione połączenie właściciela. Nie deklaruje się zielonego CI na podstawie testów lokalnych.

Po testach zakończono własne procesy aplikacji i brokera. Pliki obu SQLite oraz stan brokera zachowano. Nie wdrożono aplikacji publicznie. Pełny cykl ręcznych decyzji E1/E2 i zwolnienia partii pozostaje poza wykonaną implementacją.

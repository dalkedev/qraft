# Pierwsze kroki w kodzie

Uruchom `npm run dev` w katalogu głównym. W panelu wybierz „Niezgodna partia”, wyślij komunikat i otwórz wynik P1. Wilgotność 0,280% powinna przekroczyć limit 0,200%.

## Czytanie przepływu

1. `src/Qraft.Contracts/Messages.cs` — dane wysyłane w obie strony.
2. `src/Qraft.Simulator/ScenarioFixtures.cs` — przykładowe dane źródłowe.
3. `src/Qraft.Worker/InspectionConsumer.cs` — odebranie JSON i wywołanie procesora.
4. `src/Qraft.Application/InspectionProcessor.cs` — walidacja i mapowanie do domeny.
5. `src/Qraft.Domain/QualityEvaluation.cs` — właściwe reguły oceny.
6. `src/Qraft.Infrastructure/EfQualityStore.cs` — zapis wyniku i zadania odpowiedzi.

## Następne zadania

- Dodać model partii ze stanami i metodami dozwolonych przejść. Zacząć od testu: samo pozytywne E1 nie pozwala zwolnić partii.
- Dodać decyzję po E1 oraz opis przygotowania wymagany przed E2. Dopiero potem połączyć model z importem wiadomości.
- Dodać poprawę i następne podejście tego samego etapu, zachowując historię.
- Rozbudować API i formularze Vue o ręczną pracę na partiach i raport całego procesu.
- Rozszerzyć dane symulatora o powiązane E1/E2 oraz opóźnienia i awarie kontrolowane przez użytkownika.

Przy każdej zmianie reguły dopisz test w `tests/Qraft.UnitTests`. Przy zmianie wiadomości zaktualizuj producenta, procesor, odbiorcę wyników oraz parser `apps/web/src/contracts.ts`.

## Migracje

```sh
dotnet ef migrations add NazwaZmiany --project src/Qraft.Infrastructure --startup-project src/Qraft.Infrastructure
dotnet ef migrations add NazwaZmiany --project src/Qraft.Simulator
```

Modyfikuj tylko migrację właściwej bazy. Migracje są wykonywane podczas startu aplikacji. W tym środowisku działa jeden symulator i jeden worker; nie uruchamiaj drugiego symulatora z inną bazą na tej samej kolejce odpowiedzi.

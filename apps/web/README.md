# Qraft Lab - frontend

Lokalny panel Vue 3 i TypeScript do generowania komunikatów przez symulator i oglądania odpowiedzi Qraft. To punkt startowy aplikacji; dane zawsze pochodzą z uruchomionych usług.

Wymagany Node.js **22.22.3**. W tym katalogu:

```sh
npm ci
npm run dev
```

Panel: `http://127.0.0.1:5173`. Vite przekazuje `/api` do Qraft na porcie 5280, `/simulator-api` do symulatora na 5281, a dwa endpointy gotowości do ich `/health/ready`.

```sh
npm test
npm run build
```

Odświeżanie wykonuje jeden zestaw odczytów co najmniej co 2 sekundy, po zakończeniu poprzedniego. Ukryta karta wstrzymuje odczyty. Przycisk pozwala wstrzymać je ręcznie. Każdy odczyt ma limit 8 sekund; opuszczenie widoku anuluje aktywne żądania. Operacje POST nie są automatycznie ponawiane.

Status `Processed` oznacza przetworzony komunikat. Osobne pole `outcome` określa zgodność pomiarów. `Rejected` oznacza odrzucony komunikat, a nie decyzję o odrzuceniu partii. Nieznany status lub wersja kontraktu powoduje jawny błąd odpowiedzi API.

Ponowne wysłanie używa pierwotnego `messageId` z rekordu symulatora. Dla komunikatu oczekującego na publikację przycisk jest zablokowany; istniejący rekord nadal oczekuje na brokera.

`vite preview` pokazuje statyczny build bez dev proxy. Do pracy z lokalnymi API używaj `npm run dev` albo skonfiguruj takie same przekierowania w docelowym serwerze plików.

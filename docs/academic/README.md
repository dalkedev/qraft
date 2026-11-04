# Dokumentacja akademicka Qraft

`Qraft_specyfikacja.docx` jest edytowalnym dokumentem, a PDF powstaje przez renderowanie tego samego DOCX. `content.json` zawiera polską treść, `build_document.py` generuje DOCX i cztery rysunki PNG. Rysunki dotyczą bieżącej architektury, trwałości, interfejsu oraz wyraźnie oznaczonego planowanego procesu E1/E2.

Generator wymaga Python 3 z `python-docx` i `Pillow` oraz fontów DejaVu. Uruchom `python build_document.py` w tym katalogu. PDF wymaga osobnego renderowania DOCX i kontroli wizualnej stron. Raport weryfikacji repozytorium zawiera faktycznie wykonane testy; generator nie uruchamia testów aplikacji.

Aktualne artefakty: [DOCX](Qraft_specyfikacja.docx), [PDF](Qraft_specyfikacja.pdf). Wyniki: [WERYFIKACJA.md](../WERYFIKACJA.md). Po każdej zmianie treści należy ponowić generowanie, renderowanie i kontrolę wizualną; sam generator nie odświeża PDF.

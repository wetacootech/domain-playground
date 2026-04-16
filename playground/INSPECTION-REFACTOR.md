# Inspection — Refactoring da review 2026-04-13

## Cosa è cambiato

L'Inspection non è più un tipo di WorkOrder. È un **AR separato** in Operational con ciclo di vita proprio.

## Motivo

Il sopralluogo:
- Non genera Shift (niente Execution BC)
- Non entra nelle Mission (niente pianificazione strutturata)
- Ha UI e flusso completamente diversi dagli operativi
- Il business non ha ancora regole strutturate su schedulazione e responsabilità
- L'unico trigger di completamento è il questionario compilato

Forzarlo dentro WorkOrder creava troppe eccezioni (state machine ridotta, niente Mission, niente Shift).

## Struttura attuale

### Inspection (AR, solo in Operational — §4.8 in DDD5_DomainModel.md)

```
Inspection (AR, Operational)
├── ServiceBooked Id
├── QuestionnaireId
├── Caratteristiche (VO)
├── Data richiesta
├── Documenti
├── Stato (Da completare / Completato / Annullato)
```

**Non esiste un Inspection AR in Commercial.** Commercial ha solo i riferimenti sul ServiceBooked (`Inspection Id`, `QuestionnaireId`). Le risposte al questionario vivono nel Questionnaire (AR in Commercial), non nell'Inspection.

### State machine

```
Da completare ──(questionario compilato)──► Completato
Qualsiasi ──(annullato)──► Annullato
```

### Flusso

1. Commercial richiede sopralluogo → ServiceBooked passa a `In attesa sopralluogo`
2. Operational crea Inspection in `Da completare`
3. Operatore compila questionario (assegnazione informale, no Mission/Shift)
4. Inspection → `Completato` → Operational comunica risultati a Commercial
5. Commercial aggiorna Questionnaire, ServiceBooked torna a `Da accettare`

### Cosa NON fa

- Non genera Shift
- Non coinvolge Execution BC
- Non entra nelle Mission/Planning
- Non ha ServiceType, ServiceTimes, ServiceAddress strutturati

## WorkOrder

Torna a **2 tipi soli**: `commercial` | `operational`. Il tipo `inspection` è stato rimosso.

## Questionnaire

Ora è **AR in Commercial** (non più nello Shared Kernel). Commercial lo crea, lo verifica. Le risposte arrivano via evento quando l'Inspection in Operational viene completata.

## Riferimenti

- `DDD5_DomainModel.md` §4.8 (Inspection AR)
- `DDD5_DomainModel.md` §3 (Shared Kernel eliminato)
- `DDD5_DomainModel.md` §10d (mappa eventi)
- `Diagramma TO BE DDD 7.drawio.xml` (diagramma aggiornato)

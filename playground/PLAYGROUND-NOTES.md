# WeTacoo Domain Playground — Note di Lavoro

**Data ultimo aggiornamento:** 2026-04-14 notte
**Stato:** funzionante, pubblicato in `dist/`, **180 test xUnit verdi**, 13 scenari UC completi, build 0 errori.

---

## Aggiornamento 2026-04-14 (TL;DR)

Sessione lunga con molte iterazioni. Cambiamenti principali rispetto al 2026-04-10:

### Refactor architetturali
- **ServiceDossier eliminato** (review 2026-04-13). Dati distribuiti tra ServiceBooked (Commercial, 6 stati) e WorkOrder (Operational, 8 stati), ognuno con state machine indipendente. Sincronizzazione via eventi cross-BC.
- **Inspection AR** separato in Operational (non più tipo di WorkOrder). Non genera Mission/Shift.
- **Questionnaire AR** in Commercial (non più in Shared Kernel, eliminato).
- **ServiceEntry senza state machine** (review 2026-04-14): flag `Completed: bool` + `CompletedAt`. No Start/MarkPartial/MarkFailed/Cancel. Esiti parziali vivono nel payload `ServiceEntryOutcome` alla chiusura Shift Sospeso.
- **BC pages read-only**: BCCommercial/Operational/Execution/Financial/Marketing/Identity/Happiness sono di sola ispezione. TUTTE le azioni vivono nel Workspace (Home.razor).

### Lingua enum
- **Status/lifecycle in inglese** (per consistency idiomatica C#): DealStatus, LeadStatus, QuotationStatus, ObjectStatus, PaymentStatus, ChargeStatus, OperationStatus, ServiceBookedStatus (ToAccept/PendingInspection/ToComplete/Ready/Completed/Cancelled), WorkOrderStatus (Completing/ToSchedule/Scheduled/InExecution/Paused/ToVerify/Concluded/Cancelled), Shift.Status stringhe (Created/InProgress/Paused/Suspended/Completed/Cancelled).
- **Business type in italiano** (termini di dominio business): ServiceBookedType (Ritiro/Consegna/Smaltimento), ServiceTypeEnum (+Sopralluogo/Trasferimento/Trasbordo), ServiceEntryType (+TraslocoRitiro/TraslocoConsegna/Sopralluogo), TaskType (Censimento/Smontaggio/Imballaggio/Facchinaggio/Carico/Scarico/Disimballaggio/Rimontaggio/Ingresso/Uscita/Trasbordo/Pausa/Movimento), OperationType (RitiroCliente/ConsegnaCliente/ScaricoVeicolo/...).
- **Glossary.razor** con traduzioni italiane inline accanto agli stati inglesi (formato `EN (IT)`).

### Flusso Quotation
1. Creazione → `Draft`
2. **Conferma (Bozza → In lavorazione)** → `InProgress`
3. **Finalizza e accetta (cliente)** [singolo bottone] → Finalize + cascade `AcceptQuotation` (Questionnaire + WorkOrder + Payment + ActivePlan se DraftPlan). Nessun passaggio extra.
4. Fallback "Accetta" per Quotation già Finalizzate via scenari (cascade non ancora girata).

### Deal type determinato a runtime
Il Deal nasce sempre `OneOff`. All'accettazione della prima Quotation, se contiene un `DraftPlan` viene promosso a `Recurring` (Deposito). Non c'è più selezione di tipo in creazione Deal — solo l'area di riferimento.

### Trasloco = composizione (bottone "+ Trasloco")
Non è un tipo di ServiceBookedType. È Ritiro + Consegna nella stessa Quotation. Pulsante dedicato nel form Quotation. A runtime, se OneOff con Pickup+Delivery, i relativi WO ricevono `IsTrasloco = true` sul `ServiceTypeVO`.

### UC-13 auto-detect (cancellazione con parte fatta)
`CancelServiceBooked` distingue:
- Esecuzione non avviata (WO Completing/ToSchedule/Scheduled) → Annullamento pieno (SB Cancelled, WO Cancelled, Shift Created→Cancelled, altri Shift→Suspended).
- Esecuzione avviata (WO InExecution/Paused) → **Chiusura Anticipata UC-13**: WO → ToVerify (verrà completato), SB resta nel suo stato corrente (diverrà Completed via cascade), Shift attivi → Suspended, Shift Created → Cancelled. Deal Recurring con ActivePlan resta Active (il piano prosegue con gli oggetti già ritirati).

### Intervento risolto su Deal
Il pannello "N servizi in pausa — decisione richiesta" vive sulla card del Deal (non sul WO). Per ogni WO Paused mostra nota testuale + 3 bottoni: Riprogramma/Riprendi/Chiudi. Emette `InterventoRisoltoEvent`.

### WorkOrder operativo da Workspace
Top bar: bottone "Nuovo WO operativo" apre form inline per creare un WorkOrder di tipo `Operational` (Trasferimento/Trasbordo), parte subito in `ToSchedule` (salta Completing). Appare nel pannello "Mission Operative" in fondo alla center column, include Shift azionabili.

### Persistenza cross-navigazione
`PlaygroundState` (singleton DI) memorizza:
- `SelectedLeadId`: cliente selezionato Workspace
- `CompletedScenarioSteps: HashSet<string>`: tracking step scenari
- `CachedScenarios: Dictionary<string, List<ScenarioStep>>`: preserva le closure locali degli scenari cross-navigazione

Reset solo su click esplicito "Reset Playground" / "Reset Completo".

### Task extra esplicito
Bottone "+ Task extra" sullo Shift InProgress (niente più random). Tooltip esplica che si tratta di "Attivita' svolte dal team non previste dal servizio venduto, saranno rimeditate da Commercial in fase di verifica".

### Multi-Mission per Service
Un WO può essere in N Mission (multi-giorno, trasbordi, split volume). Il WO transita a `ToVerify` solo quando TUTTI gli Shift associati sono `Completed`. Form Mission in Workspace con multi-checkbox WO, multi-vehicle, team selector, timeslot editabile.

### Scenari 1:1 UC + test
- `WeTacoo.Tests/`: 180 test xUnit (14 file, UC1-UC13 + EndToEnd state machine). Domain-only, indipendenti.
- `ScenarioEngine.cs` (~2800 righe): 13 scenari (`UC-N: <titolo>`). 4 hanno `BranchPointNote` (UC-6/7/12/13) che limita l'auto-esecuzione al punto deterministico dell'UC, oltre l'utente prosegue manualmente nel Workspace per replicare qualunque outcome.
- Bottone UI "Esegui fino al punto di scelta (step N)" attivo sui branch point.

---

## Note storiche (pre-2026-04-14)

La documentazione che segue riflette lo stato antecedente. Alcune decisioni sono state superate (vedi aggiornamento sopra), altre sono ancora valide come contesto storico.

---

## Fonte di verita'

**L'UNICO documento di riferimento per il dominio TO BE e':**
- `DDD5_DomainModel.md` (nella cartella `refactoring/`)
- `Events.drawio.xml` (nella cartella `refactoring/`)

**Non usare** `wetacoo-domain-model-tobe.md`, `UseCases-Solutions.md` o altri documenti come fonte di verita' sul TO BE. Questi documenti sono storici e in parte superati dal DDD5.

---

## Decisioni architetturali prese durante lo sviluppo

### Order NON esiste
Nel DDD5 non c'e' nessuna entita' "Order". Il flusso commerciale e':
```
Lead → Deal (con Quotation[]) → Quotation accettata → ServiceBooked estratti → ServiceDossier + WorkOrder
```
L'Order e' stato **completamente rimosso** dal playground (dominio, eventi, UI, scenari).

### Qualifica e' sul Deal, non sul Lead
Confermato da Events.drawio: "Qualifica deal" e' un'azione sulla trattativa. Il Lead ha uno stato derivato dai Deal associati. La state machine del Deal e' stata aggiunta al DDD5 (sezione 2.1) durante questa sessione.

### Stati Deal (da Events.drawio, aggiunti al DDD5 sezione 2.1)
Da qualificare → Qualificato → In trattativa → Convertito → Attivo → Concluso
+ Non convertito (scarto pre-conversione)
+ Annullato (cancellazione post-conversione)

### Stati WorkOrder (da Events.drawio, aggiunti al DDD5 sezione 4.1)
Da completare → Da programmare → Programmato → In esecuzione → Da verificare → Concluso
+ Annullato (soft delete, da §10b)
Corrispondono 1:1 con Events.drawio. Verificato nella sessione del 2026-04-09.

### Stati Quotation (da Events.drawio, aggiunti al DDD5 sezione 2.1)
Bozza → In lavorazione → Finalizzato → Da verificare → Completato
+ Da adeguare (post-esecuzione con differenze)
+ Archiviato (sostituita)
+ Annullato
La Quotation accettata corrisponde a Finalizzato (o oltre).

### Questionnaire per ServiceBooked
Alla accettazione della Quotation, per ogni ServiceBooked viene creato un Questionnaire con le domande dal catalogo QuestionTemplates. Il WorkOrder parte in "Da completare" — passa a "Da programmare" quando il questionario e' compilato e verificato (bottone "Pronto").

### Tipi Deal
- **Recurring** (Deposito): ha ActivePlan, addebiti ricorrenti
- **OneOff** (Trasloco): nessun ActivePlan, si chiude quando i servizi sono completati

### Quotation e Piano
- **DraftPlan** (0..N per Quotation): proposta immutabile
- **ActivePlan** (0..1 per Deal): piano in produzione, evolve nel tempo
- Solo la PRIMA Quotation accettata con DraftPlan crea l'ActivePlan
- Le Quotation successive (servizi aggiuntivi su deal attivo) NON hanno DraftPlan
- L'ActivePlan si chiude quando Object OnWarehouse per quel Deal = 0

### Trasloco = composizione
Non e' un tipo di Service. E' Ritiro + Consegna nella stessa Quotation. I WorkOrder hanno `ServiceType.IsTrasloco = true` per guidare i ServiceEntry.Type (TraslocoRitiro, TraslocoConsegna).

### Catena cross-BC
```
Execution (registra fatti) → Operational (verifica, qualifica impatto) → Commercial (decide adeguamenti, comanda Financial)
```
Operational non e' un relay. Financial esegue, non calcola.

### Chiusura ActivePlan
Basata su **conteggio Object OnWarehouse per il Deal**, non sul flag Parziale. Dopo ogni consegna/smaltimento: se count = 0 → plan si chiude → deal si chiude.

---

## Struttura del progetto

```
playground/
├── WeTacoo.Domain/                    # Modello DDD (riusabile)
│   ├── Common/AggregateRoot.cs        # Base classes con ID mnemonici (lead-001, deal-002...)
│   ├── Events/DomainEvents.cs         # Tutti gli eventi (NO Order events)
│   ├── Commercial/                    # Lead, Deal, Quotation, ProductTemplate, Salesman, Coupon, QuestionTemplate
│   ├── SharedKernel/                  # ServiceDossier (state machine), Questionnaire, Inspection, Area, ObjectTemplate
│   ├── Operational/                   # WorkOrder, Planning (Mission, Team, Resource), Slot, Warehouse, Vehicle, Operator
│   ├── Execution/                     # Shift (ServiceEntry, Task), PhysicalObject, WarehouseOperation, Label
│   ├── Financial/                     # Payment (Charge, SimplifiedProduct), FinancialClient
│   ├── Identity/                      # User, AdminUser, OperatorUser
│   ├── Marketing/                     # MarketingClient
│   └── Happiness/                     # HappinessClient
│
├── WeTacoo.Playground.Web/
│   ├── Services/
│   │   ├── PlaygroundState.cs         # Stato in-memory + seed data + reset
│   │   └── ScenarioEngine.cs          # 7 scenari automatici
│   └── Components/Pages/
│       ├── Home.razor                 # WORKSPACE principale (flusso centrato sul cliente)
│       ├── QuickActionDialog.razor    # Wizard "Nuovo Deposito" / "Nuovo Trasloco"
│       ├── Scenarios.razor            # Scenari step-by-step
│       ├── Events.razor               # Event log completo
│       ├── Glossary.razor             # Glossario entita'/stati/flussi/regole
│       ├── BC*.razor                  # Pagine ispettive per BC (sotto "Ispeziona BC")
│       └── Layout/NavMenu.razor       # Navigazione
│
├── dist/                              # Exe pubblicato (self-contained)
├── PLAYGROUND-NOTES.md                # Questo file
└── SCENARIOS-TODO.md                  # Scenari originali (tutti implementati)
```

---

## Seed data (dati di partenza)

All'avvio il playground ha gia':
- 3 Aree (Milano, Roma, Torino)
- 3 Magazzini (uno per area)
- 4 Veicoli
- 7 Operatori + 3 Commerciali + 1 Admin
- 8 ProductTemplates (deposito base/premium/XL, ritiro, consegna, smaltimento, supplemento, imballaggio)
- 5 QuestionTemplates
- 3 Coupon
- 18 ObjectTemplates (catalogo calcolatore volume)
- ~20 Slot (14 giorni, Milano + Roma)
- 3 Lead pre-popolati (Anna Verdi, Marco Neri, Giulia Romano) con Identity e Marketing
- ID mnemonici: lead-001, deal-002, quot-003, wo-004... (si azzerano al Reset)

---

## 7 scenari implementati

| # | Nome | Steps | Cosa testa |
|---|---|---|---|
| 1 | Catena Completa Deposito | 12 | Flusso end-to-end con mismatch e adeguamento |
| 2 | Consegna Parziale | 4 | Quotation aggiuntiva su deal attivo |
| 3 | Deal Non Convertito | 3 | Scarto deal |
| 4 | Trasloco Punto-Punto | 7 | Composizione ritiro+consegna, Mission unica, 2 firme |
| 5 | Oggetto Rifiutato | 4 | Chiusura ActivePlan basata su conteggio Object |
| 6 | Smaltimento + Consegna | 4 | 2 ServiceBooked diversi, stati finali Object distinti |
| 7 | Cancellazione a Meta' | 6 | Soft delete, riconsegna, penali |

---

## 13 Use Cases coperti

Tutti i 13 UC di `UseCases-Solutions.md` sono testabili dal Workspace:
UC-1 (deposito), UC-2 (trasloco), UC-3 (consegna parziale), UC-4 (ritiro multi-giorno via volume %),
UC-5 (sopralluogo), UC-6 (problema esecuzione), UC-7 (task extra), UC-8 (oggetto rifiutato),
UC-9 (2 depositi 2 citta'), UC-10 (trasbordo), UC-11 (smaltimento+consegna), UC-12 (self-service),
UC-13 (cancellazione)

---

## Feature del Workspace (Home.razor)

### Barra in alto
- "Nuovo Deposito" / "Nuovo Trasloco" → wizard dialog
- Reset

### Colonna sinistra
- Lista clienti selezionabili
- Nuovo lead manuale

### Colonna centrale (flusso per cliente selezionato)
1. **Lead card** con status
2. **Deal** (collassabili) con azioni contestuali (Qualifica, Scarta, Richiedi Sopralluogo)
3. **Quotation** (collassabili dentro Deal) con edit inline (prodotti, servizi, piano), Accetta
4. **WorkOrder** con azioni stato (Pronto, Programma, Avvia, Completa, Concludi) + Crea Planning+Mission con **date picker** e **volume %** + Crea Trasbordo
5. **Shift** con azioni (Avvia, Simula, Completa, Segnala Problema) + display task extra
6. **Oggetti** tabella con stato
7. **Pagamenti** con addebiti eseguibili

### Colonna destra
- Eventi filtrati per cliente, con descrizione umana in grassetto + dettaglio tecnico in corsivo

---

## Problemi noti / cose da migliorare

### Funzionali
- La simulazione esecuzione crea oggetti random — il selettore oggetti e' disponibile nella creazione Quotation per consegne/smaltimenti/donazioni
- Il flusso sopralluogo (UC-5) crea il WorkOrder ma non ha ancora la UI per **compilare le risposte al questionario** durante lo Shift (il questionario viene creato ma le risposte sono tutte null durante l'esecuzione sul campo)

### Regole di integrita' applicate
- **ServiceDossier**: non creabile manualmente — nasce solo dalla cascata AcceptQuotation (uno per ServiceBooked)
- **Shift per Mission**: non duplicabile — il bottone "Crea Planning + Mission + Shift" sparisce dopo la prima creazione
- **DraftPlan su deal attivo**: non proponibile — il form piano appare solo se il Deal non ha gia' un ActivePlan

### Tecnici
- I warning MUD0002 (Dense attribute su MudNumericField/MudDatePicker) sono cosmetici
- Il DatePicker per la data Planning usa `Editable="true"` per permettere digitazione diretta
- La pagina Glossario potrebbe essere arricchita con diagrammi Mermaid interattivi

## Feature implementate (2026-04-10)

1. **Fix 1-3 dal REVIEW-FIXES.md** — Tutti implementati (Deal.Activate per OneOff, chiusura ActivePlan, ServiceEntry.Cancelled)
2. **Compilazione questionario** — UI inline per rispondere alle domande, verifica questionario sblocca WO da "Da completare" a "Da programmare"
3. **Selettore oggetti** — checkbox sugli Object OnWarehouse per consegne/smaltimenti/donazioni nella creazione Quotation, con selezione salvata su ServiceBooked.SelectedObjectIds
4. **Flusso adeguamento guidato** — pannello post-mismatch su WO con delta volume, edit recuperato, bottone "Applica Adeguamento" che aggiorna ActivePlan e chiude ServiceDossier
5. **Assegnazione WO a Mission esistente** — selettore Mission disponibili accanto al "Crea Planning", aggiunge ServiceRef alla Mission e ServiceEntry allo Shift
6. **Tracciabilita' cross-BC** — pannello collassabile che mostra la catena ServiceBooked → Questionnaire → ServiceDossier → WorkOrder → Mission → Shift → Payment con chip colorati per stato

## Prossimi passi suggeriti

1. **Compilazione questionario durante Shift** — le risposte vengono inserite pre-esecuzione; servirebbe anche la possibilita' di compilarle durante l'esecuzione (sopralluogo)
2. **Simulazione esecuzione selettiva** — permettere di scegliere quanti/quali oggetti creare (non solo random)
3. **Diagrammi Mermaid interattivi** — nella pagina Glossario

---

## Come pubblicare

```bash
cd playground
dotnet publish WeTacoo.Playground.Web/WeTacoo.Playground.Web.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./dist
```

Poi patchare `dist/appsettings.json` con:
```json
"Kestrel": { "Endpoints": { "Http": { "Url": "http://localhost:5100" } } }
```

L'utente lancia `dist/Avvia Playground.bat`.

---

## Come sviluppare

```bash
cd playground/WeTacoo.Playground.Web
dotnet run --launch-profile http
```

Apre su `http://localhost:5277`. DetailedErrors e' abilitato in Program.cs.

Pattern per state subscription nei componenti Blazor:
```csharp
private Action _onStateChanged = null!;
protected override void OnInitialized() { _onStateChanged = () => InvokeAsync(StateHasChanged); State.OnStateChanged += _onStateChanged; }
public void Dispose() => State.OnStateChanged -= _onStateChanged;
```

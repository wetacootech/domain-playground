namespace WeTacoo.Domain.Operational.Enums;

/// <summary>
/// State machine del WorkOrder (DDD5 §10c WORKORDER, review 2026-04-13).
/// Traccia il ciclo di vita operativo: pianificazione ed esecuzione.
/// </summary>
public enum WorkOrderStatus
{
    Completing,    // Stato iniziale. WO creato, Commercial sta completando questionario/dati. Ops puo' creare Mission ma non confermarle
    ToSchedule,    // ServiceBooked Ready, dati completi. Il planner puo' confermare le Mission
    Scheduled,     // Mission create e confermate. In attesa di esecuzione
    InExecution,   // Shift avviato, operatori al lavoro
    Paused,        // Problema che richiede decisione Commercial
    ToVerify,      // Ops verifica fatti ricevuti da Execution
    Concluded,     // Terminale positivo
    Cancelled      // Terminale negativo
}

// DDD5 §4.1 (review 2026-04-17): 3 tipi.
// - Commercial: generato dall'accettazione di un ServiceBooked venduto (quotation finalizzata)
// - Operational: creato dal planner per attivita' interne (trasferimenti, trasbordi, magazzino)
// - Sopralluogo: creato su richiesta "Richiedi sopralluogo" da un ServiceBooked. Non e' un venduto;
//   durante l'esecuzione l'operatore compila il Questionnaire; a fine WO il ServiceBooked torna ToAccept.
public enum WorkOrderType { Commercial, Operational, Sopralluogo }

public enum ServiceTypeEnum { Ritiro, Consegna, Smaltimento, Sopralluogo, Trasferimento, Trasbordo }

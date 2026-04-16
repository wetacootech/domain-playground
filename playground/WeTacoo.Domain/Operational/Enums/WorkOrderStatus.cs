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

public enum WorkOrderType { Commercial, Operational }

public enum ServiceTypeEnum { Ritiro, Consegna, Smaltimento, Sopralluogo, Trasferimento, Trasbordo }

namespace WeTacoo.Domain.Commercial.Enums;

/// <summary>
/// State machine del ServiceBooked (DDD5 §10c SERVICEBOOKED, review 2026-04-17).
/// Traccia il ciclo di vita commerciale: "devo fare qualcosa o no?"
/// WaitingInspection: sopralluogo richiesto, WorkOrder dedicato in esecuzione.
/// </summary>
public enum ServiceBookedStatus
{
    ToAccept,            // Quotation non finalizzata, attende accettazione
    WaitingInspection,   // Sopralluogo richiesto (WorkOrder Sopralluogo in corso). Sales attende risultati questionario.
    ToComplete,          // Sales deve agire: questionario, problema, rinegoziazione
    Ready,               // Nessuna azione Commercial — palla a Operational
    Completed,           // Terminale positivo
    Cancelled            // Terminale negativo
}

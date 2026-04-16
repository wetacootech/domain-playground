namespace WeTacoo.Domain.Commercial.Enums;

/// <summary>
/// State machine del ServiceBooked (DDD5 §10c SERVICEBOOKED, review 2026-04-13).
/// Traccia il ciclo di vita commerciale: "devo fare qualcosa o no?"
/// </summary>
public enum ServiceBookedStatus
{
    ToAccept,            // Quotation non finalizzata, attende accettazione
    PendingInspection,   // Sales attende risultati Inspection
    ToComplete,          // Sales deve agire: questionario, problema, rinegoziazione
    Ready,               // Nessuna azione Commercial — palla a Operational
    Completed,           // Terminale positivo
    Cancelled            // Terminale negativo
}

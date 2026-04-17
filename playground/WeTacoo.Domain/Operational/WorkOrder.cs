namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Operational.Enums;
using WeTacoo.Domain.Operational.ValueObjects;

/// <summary>
/// WorkOrder — unita' di lavoro pianificabile in Operational.
/// Ha propria state machine (DDD5 §10c WORKORDER, review 2026-04-13):
///   Completing -> ToSchedule -> Scheduled -> InExecution -> ToVerify -> Concluded
///   InExecution -> Paused (serve decisione Commercial)
///   Paused -> ToSchedule / InExecution / ToVerify (InterventoRisolto)
/// Nota: in Completing le Mission possono essere create ma NON confermate.
/// </summary>
public class WorkOrder : AggregateRoot
{
    public WorkOrder() { Id = NextId("wo"); CreatedAt = DateTime.UtcNow; }
    public DateTime CreatedAt { get; set; }
    public WorkOrderType Type { get; set; } = WorkOrderType.Commercial;
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Completing;
    public string? ServiceBookedId { get; set; }  // riferimento diretto al ServiceBooked in Commercial (per tipo commercial)
    public ServiceTypeVO ServiceType { get; set; } = new(ServiceTypeEnum.Ritiro, false, false, null);
    public CommercialData? Commercial { get; set; }
    public OperationalDataVO? Operational { get; set; }
    public string? ServiceAddress { get; set; }
    public string? DestinationAddress { get; set; }
    public string? Notes { get; set; }
    public decimal EstimatedVolume { get; set; }
    public decimal ActualVolume { get; set; }
    public string? ContactName { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public string? ScheduledSlot { get; set; }
    public List<string> StatusHistory { get; private set; } = [];
    /// <summary>
    /// DDD5 §10d (review 2026-04-17): true quando la pausa richiede decisione Commercial (serve escalation).
    /// False = gestione interna Ops (planner risolve senza coinvolgere Sales).
    /// Significativo solo quando Status == Paused. Resettato a false all'uscita dalla pausa.
    /// </summary>
    public bool PausedForCommercial { get; private set; }

    private void SetStatus(WorkOrderStatus newStatus, string reason)
    {
        StatusHistory.Add($"{DateTime.UtcNow:u} {Status} -> {newStatus}: {reason}");
        Status = newStatus;
        Touch();
    }

    // Completing -> ToSchedule (servizio pronto da Commercial)
    public void ServizioPronto(string by)
    {
        if (Status == WorkOrderStatus.Completing)
            SetStatus(WorkOrderStatus.ToSchedule, $"Servizio pronto — {by}");
    }

    // ToSchedule -> Scheduled
    public void Programma(string by)
    {
        if (Status == WorkOrderStatus.ToSchedule)
            SetStatus(WorkOrderStatus.Scheduled, $"Programmato da {by}");
    }

    // Scheduled -> ToSchedule (riprogramma)
    public void Riprogramma(string by)
    {
        if (Status == WorkOrderStatus.Scheduled)
            SetStatus(WorkOrderStatus.ToSchedule, $"Riprogrammato da {by}");
    }

    // Scheduled -> InExecution (operazione avviata)
    public void AvviaEsecuzione(string by)
    {
        if (Status == WorkOrderStatus.Scheduled)
            SetStatus(WorkOrderStatus.InExecution, $"Avviato da {by}");
    }

    // InExecution -> ToVerify (operazione completata)
    public void CompletaEsecuzione(string by)
    {
        if (Status == WorkOrderStatus.InExecution)
            SetStatus(WorkOrderStatus.ToVerify, $"Completato da {by}");
    }

    // InExecution -> ToVerify (chiusura anticipata decisa da Commercial, UC-13)
    public void ChiusuraAnticipata(string motivo)
    {
        if (Status == WorkOrderStatus.InExecution)
            SetStatus(WorkOrderStatus.ToVerify, $"Chiusura anticipata Commercial: {motivo}");
    }

    // InExecution -> ToSchedule (operazione interrotta, solo problema operativo)
    public void InterrompiEsecuzione(string by)
    {
        if (Status == WorkOrderStatus.InExecution)
            SetStatus(WorkOrderStatus.ToSchedule, $"Interrotto da {by} — riprogrammare");
    }

    // InExecution -> Paused. forCommercial=true segnala che serve decisione Sales (scattera' RichiedeInterventoEvent).
    // forCommercial=false = gestione interna Ops (planner risolve senza coinvolgere Commercial).
    public void MettiInPausa(string motivo, bool forCommercial = true)
    {
        if (Status != WorkOrderStatus.InExecution) return;
        PausedForCommercial = forCommercial;
        var tag = forCommercial ? "serve Commercial" : "gestione Ops";
        SetStatus(WorkOrderStatus.Paused, $"In pausa ({tag}): {motivo}");
    }

    // ToSchedule -> Paused (non programmabile). forCommercial=true escalation a Sales, false = Ops riprova.
    public void NonProgrammabile(string motivo, bool forCommercial = true)
    {
        if (Status != WorkOrderStatus.ToSchedule) return;
        PausedForCommercial = forCommercial;
        var tag = forCommercial ? "serve Commercial" : "gestione Ops";
        SetStatus(WorkOrderStatus.Paused, $"Non programmabile ({tag}): {motivo}");
    }

    // Paused -> ToSchedule. Usato sia da Commercial (InterventoRisolto riprogramma — DEPRECATO 2026-04-17)
    // sia da Ops (gestione interna: planner decide di riprogrammare).
    public void InterventoRisoltoRiprogramma(string note)
    {
        if (Status != WorkOrderStatus.Paused) return;
        PausedForCommercial = false;
        SetStatus(WorkOrderStatus.ToSchedule, $"Riprogramma: {note}");
    }

    // Paused -> InExecution. Usato da Commercial "Riprendi servizio" (review 2026-04-17, opzione a)
    // o da Ops per gestione interna se team ancora sul posto.
    public void InterventoRisoltoRiprendi(string note)
    {
        if (Status != WorkOrderStatus.Paused) return;
        PausedForCommercial = false;
        SetStatus(WorkOrderStatus.InExecution, $"Riprendi: {note}");
    }

    // Paused -> ToVerify (InterventoRisolto "chiudi"). Canale storico, non piu' esposto a Commercial UI (review 2026-04-17).
    // Ancora usabile per UC-13 (ChiusuraAnticipata invocata su WO in pausa con esecuzione parziale).
    public void InterventoRisoltoChiudi(string note)
    {
        if (Status != WorkOrderStatus.Paused) return;
        PausedForCommercial = false;
        SetStatus(WorkOrderStatus.ToVerify, $"Intervento risolto (chiudi): {note}");
    }

    // ToVerify -> Concluded
    public void VerificaEConcludi(string by)
    {
        if (Status == WorkOrderStatus.ToVerify)
            SetStatus(WorkOrderStatus.Concluded, $"Verificato e concluso da {by}");
    }

    // Qualsiasi -> Cancelled
    public void Annulla(string by)
    {
        SetStatus(WorkOrderStatus.Cancelled, $"Annullato da {by}");
    }
}


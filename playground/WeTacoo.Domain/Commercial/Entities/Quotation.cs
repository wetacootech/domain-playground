namespace WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.ValueObjects;

public class Product : Entity
{
    public Product() { Id = NextEntityId("qprod"); }
    public string ProductTemplateId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
}

public class DraftPlan : Entity
{
    public DraftPlan() { Id = NextEntityId("dplan"); }
    public string Description { get; set; } = "";
    public decimal MonthlyFee { get; set; }
    public decimal EstimatedM3 { get; set; }
    public string? AreaId { get; set; }
}

/// <summary>
/// ServiceBooked — rappresentazione commerciale del servizio venduto.
/// State machine da DDD5 §10c SERVICEBOOKED (review 2026-04-13).
/// </summary>
public class ServiceBooked : Entity
{
    public ServiceBooked() { Id = NextEntityId("svc"); }
    public ServiceBookedType Type { get; set; }
    public ServiceBookedStatus Status { get; private set; } = ServiceBookedStatus.ToAccept;
    /// <summary>Modalita' self-service: cliente va al magazzino, no veicoli pianificati (vedi Q1bis DDD5).</summary>
    public bool IsAutonomous { get; set; }
    public string? WorkOrderId { get; set; }          // riferimento diretto al WorkOrder in Operational
    public string? QuestionnaireId { get; set; }
    public string? InspectionId { get; set; } // riferimento al WO tipo inspection
    public Address? ServiceAddress { get; set; }
    public Address? DestinationAddress { get; set; }
    public string Notes { get; set; } = "";
    public List<string> SelectedObjectIds { get; set; } = [];
    public DateTime? ScheduledDate { get; set; }
    public string? ScheduledSlot { get; set; }
    public List<string> StatusHistory { get; private set; } = [];

    // VO per dati completamento (write-once, ricevuto da Ops via evento ServizioCompletato)
    public CompletionRecord? CompletionData { get; set; }

    private void SetStatus(ServiceBookedStatus newStatus, string reason)
    {
        StatusHistory.Add($"{DateTime.UtcNow:u} {Status} -> {newStatus}: {reason}");
        Status = newStatus;
    }

    // ToAccept -> PendingInspection
    public void RichiediSopralluogo()
    {
        if (Status == ServiceBookedStatus.ToAccept)
            SetStatus(ServiceBookedStatus.PendingInspection, "Richiesta sopralluogo");
    }

    // PendingInspection -> ToAccept (sopralluogo completato, sales rivaluta)
    public void SopralluogoCompletato()
    {
        if (Status == ServiceBookedStatus.PendingInspection)
            SetStatus(ServiceBookedStatus.ToAccept, "Sopralluogo completato — sales rivaluta");
    }

    // ToAccept -> ToComplete (quotation finalizzata, cliente accetta)
    public void AccettaServizio()
    {
        if (Status == ServiceBookedStatus.ToAccept)
            SetStatus(ServiceBookedStatus.ToComplete, "Quotation finalizzata — completare questionario");
    }

    // ToComplete -> Ready (questionario verificato, dati completi)
    public void SegnaComePronto()
    {
        if (Status == ServiceBookedStatus.ToComplete)
            SetStatus(ServiceBookedStatus.Ready, "Questionario verificato — pronto per Operational");
    }

    // Ready -> ToComplete (problema segnalato da Operational)
    public void RichiedeIntervento(string motivo)
    {
        if (Status == ServiceBookedStatus.Ready)
            SetStatus(ServiceBookedStatus.ToComplete, $"Richiede intervento: {motivo}");
    }

    // ToComplete -> Ready (sales risolve, continua)
    public void InterventoRisolto()
    {
        if (Status == ServiceBookedStatus.ToComplete)
            SetStatus(ServiceBookedStatus.Ready, "Intervento risolto — ripronto");
    }

    // ToComplete -> Completed (sales chiude a volume ridotto)
    public void ChiudiAVolumeRidotto()
    {
        if (Status == ServiceBookedStatus.ToComplete)
            SetStatus(ServiceBookedStatus.Completed, "Chiuso a volume ridotto");
    }

    // Ready -> Completed (servizio completato da Operational)
    public void ServizioCompletato()
    {
        if (Status == ServiceBookedStatus.Ready)
            SetStatus(ServiceBookedStatus.Completed, "Servizio completato da Operational");
    }

    // Qualsiasi -> Cancelled
    public void Annulla()
    {
        SetStatus(ServiceBookedStatus.Cancelled, "Annullato");
    }
}

public class Quotation : Entity
{
    public Quotation() { Id = NextEntityId("quot"); }
    public string DealId { get; set; } = "";
    /// <summary>
    /// True solo per la prima Quotation accettata del Deal. Marcato all'accettazione, non alla creazione.
    /// Le Quotation aggiuntive (servizi successivi su Deal gia' convertito) restano IsInitial=false.
    /// </summary>
    public bool IsInitial { get; set; } = false;
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;
    public List<Product> Products { get; set; } = [];
    public List<ServiceBooked> Services { get; set; } = [];
    public List<DraftPlan> DraftPlans { get; set; } = [];
    public PaymentCondition? PaymentCondition { get; set; }
    public string? CouponCode { get; set; }
    public decimal TotalPrice => Products.Sum(p => p.Price);

    // Convenience for backward compat — accepted = Finalized or beyond
    public bool IsAccepted
    {
        get => Status is QuotationStatus.Finalized or QuotationStatus.ToVerify or QuotationStatus.ToAdjust or QuotationStatus.Completed;
        set { if (value && Status == QuotationStatus.Draft) Status = QuotationStatus.Finalized; }
    }

    // State machine from DDD5 §10c (ORDER)
    public void Confirm() { if (Status == QuotationStatus.Draft) Status = QuotationStatus.InProgress; }
    public void TakeOver() { if (Status == QuotationStatus.InProgress) Status = QuotationStatus.Draft; }
    public void Finalize() { if (Status is QuotationStatus.Draft or QuotationStatus.InProgress) Status = QuotationStatus.Finalized; }
    public void MarkToVerify() { if (Status == QuotationStatus.Finalized) Status = QuotationStatus.ToVerify; }
    public void Verify() { if (Status == QuotationStatus.ToVerify) Status = QuotationStatus.Finalized; }
    public void MarkToAdjust() { if (Status == QuotationStatus.Finalized) Status = QuotationStatus.ToAdjust; }
    public void Complete() { if (Status is QuotationStatus.Finalized or QuotationStatus.ToAdjust) Status = QuotationStatus.Completed; }
    public void Archive() { if (Status is QuotationStatus.Draft or QuotationStatus.InProgress) Status = QuotationStatus.Archived; }
    public void Cancel() { if (Status is QuotationStatus.ToVerify or QuotationStatus.Finalized) Status = QuotationStatus.Cancelled; }
}

// Value Objects
public record CompletionRecord(decimal ActualVolume, int ObjectsMoved, string? DifferencesNotes, DateTime CompletedAt) : ValueObject;

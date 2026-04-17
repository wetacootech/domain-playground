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
    public string? WorkOrderId { get; set; }          // riferimento diretto al WorkOrder commerciale in Operational
    /// <summary>
    /// DDD5 §2.2 / §4.8 (review 2026-04-17). Riferimento al WorkOrder tipo Sopralluogo collegato a questo servizio.
    /// Valorizzato da RichiediSopralluogo. Consente a Commercial di leggere esito/questionario compilato.
    /// </summary>
    public string? InspectionId { get; set; }
    // QuestionnaireId rimosso (review 2026-04-17): il Questionnaire e' unico per Quotation, non per ServiceBooked. Vedi Quotation.QuestionnaireId.
    // QuestionnaireReady spostato su Quotation (review 2026-04-17).
    /// <summary>
    /// DDD5 §2.2e (review 2026-04-16). Altri ServiceBooked appaiati per un trasloco.
    /// Ritiro.MovingIds = Consegne alimentate; Consegna.MovingIds = Ritiri di provenienza. Vuoto = standalone.
    /// </summary>
    public List<string> MovingIds { get; set; } = [];
    public Address? ServiceAddress { get; set; }
    public Address? DestinationAddress { get; set; }
    public string Notes { get; set; } = "";
    public List<string> SelectedObjectIds { get; set; } = [];
    /// <summary>
    /// Lista oggetti stimati (DDD5 §2.1 EstimatedObject, review 2026-04-17). Ogni riga
    /// referenzia un ObjectTemplate di Shared Infrastructure e porta quantita' + snapshot
    /// del volume unitario. Alternativa a DeclaredVolume: se valorizzata, EstimatedVolume
    /// e' derivato; altrimenti cade su DeclaredVolume. Immutabile dopo finalizzazione (SB.Status != ToAccept).
    /// </summary>
    public List<ServiceBookedItem> Items { get; set; } = [];
    /// <summary>Volume dichiarato spannometrico (m³) quando l'agente non dettaglia oggetti. Alternativo a Items.</summary>
    public decimal? DeclaredVolume { get; set; }
    /// <summary>Volume stimato: somma Items se presenti, altrimenti DeclaredVolume (o 0).</summary>
    public decimal EstimatedVolume => Items.Count > 0 ? Items.Sum(i => i.Quantity * i.UnitVolume) : (DeclaredVolume ?? 0m);
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

    // ToAccept -> ToComplete (quotation finalizzata, cliente accetta).
    // Se questionnaireReady (passato dal caller leggendo Quotation.QuestionnaireReady) salta ToComplete e va direttamente a Ready.
    // Non accettabile mentre in WaitingInspection: prima deve tornare ToAccept via SopralluogoCompletato.
    public void AccettaServizio(bool questionnaireReady = false)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        if (questionnaireReady)
            SetStatus(ServiceBookedStatus.Ready, "Quotation finalizzata — questionario gia' compilato in sopralluogo");
        else
            SetStatus(ServiceBookedStatus.ToComplete, "Quotation finalizzata — completare questionario");
    }

    // ToAccept -> WaitingInspection (Commercial chiede un sopralluogo).
    // Il QuestionnaireId e' sulla Quotation (unico per Quotation, review 2026-04-17) — non passato al SB.
    // Crea un WorkOrder Sopralluogo dedicato (woId).
    public void RichiediSopralluogo(string inspectionWorkOrderId)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        InspectionId = inspectionWorkOrderId;
        SetStatus(ServiceBookedStatus.WaitingInspection, $"Sopralluogo richiesto — WO {inspectionWorkOrderId}");
    }

    // WaitingInspection -> ToAccept (sopralluogo concluso, Sales rivaluta).
    // Il flag QuestionnaireReady ora vive sulla Quotation (review 2026-04-17): MarkQuestionnaireReady deve essere
    // chiamato a parte dal handler a livello di Quotation.
    public void SopralluogoCompletato()
    {
        if (Status != ServiceBookedStatus.WaitingInspection) return;
        SetStatus(ServiceBookedStatus.ToAccept, "Sopralluogo completato — questionario disponibile sulla Quotation");
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

    // ── Gestione Items (oggetti stimati) — mutabili solo mentre Status == ToAccept ──

    /// Aggiunge una riga item. No-op se il servizio non e' piu' modificabile (Status != ToAccept).
    public void AddItem(string objectTemplateId, string templateName, int quantity, decimal unitVolume)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        if (quantity <= 0 || unitVolume < 0) return;
        var existing = Items.FindIndex(i => i.ObjectTemplateId == objectTemplateId);
        if (existing >= 0)
            Items[existing] = Items[existing] with { Quantity = Items[existing].Quantity + quantity };
        else
            Items.Add(new ServiceBookedItem(objectTemplateId, templateName, quantity, unitVolume));
        DeclaredVolume = null;
    }

    public void UpdateItemQuantity(string objectTemplateId, int quantity)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        var idx = Items.FindIndex(i => i.ObjectTemplateId == objectTemplateId);
        if (idx < 0) return;
        if (quantity <= 0) Items.RemoveAt(idx);
        else Items[idx] = Items[idx] with { Quantity = quantity };
    }

    public void RemoveItem(string objectTemplateId)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        Items.RemoveAll(i => i.ObjectTemplateId == objectTemplateId);
    }

    public void ClearItems()
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        Items.Clear();
    }

    public void SetDeclaredVolume(decimal? volume)
    {
        if (Status != ServiceBookedStatus.ToAccept) return;
        if (volume.HasValue && volume.Value < 0) return;
        DeclaredVolume = volume;
        if (volume.HasValue) Items.Clear();
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
    /// <summary>
    /// DDD5 §3.1 (review 2026-04-17, XML TO BE DDD 7): il Questionnaire e' **unico per Quotation**,
    /// non per singolo ServiceBooked. Raccoglie le info di contesto commerciale/operativo per tutti i servizi.
    /// </summary>
    public string? QuestionnaireId { get; set; }

    /// <summary>
    /// DDD5 §4.8 (review 2026-04-17): true quando il Questionnaire della Quotation e' stato verificato.
    /// Settato da MarkQuestionnaireReady (es. dopo conclusione sopralluogo).
    /// Passato ai ServiceBooked al momento di AccettaServizio per decidere se saltare ToComplete.
    /// </summary>
    public bool QuestionnaireReady { get; private set; }

    public void MarkQuestionnaireReady()
    {
        QuestionnaireReady = true;
    }
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

/// <summary>
/// Riga di oggetti stimati su un ServiceBooked (DDD5 §2.1, review 2026-04-17).
/// Snapshot: TemplateName e UnitVolume sono immutabili e non seguono modifiche successive di ObjectTemplate.
/// </summary>
public record ServiceBookedItem(string ObjectTemplateId, string TemplateName, int Quantity, decimal UnitVolume) : ValueObject
{
    public decimal TotalVolume => Quantity * UnitVolume;
}

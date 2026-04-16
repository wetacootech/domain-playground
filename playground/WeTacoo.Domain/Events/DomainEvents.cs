namespace WeTacoo.Domain.Events;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
    string Description { get; }
    string SourceBC { get; }
    string TargetBC { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public abstract string Description { get; }
    public abstract string SourceBC { get; }
    public abstract string TargetBC { get; }
}

// ══════════════════════════════════════════════════
//  QUOTATION events (Commercial)
// ══════════════════════════════════════════════════
public record QuotationAcceptedEvent(string QuotationId, string DealId, string LeadId, bool IsRecurring) : DomainEvent
{
    public override string Description => $"Quotation {QuotationId} accepted on Deal {DealId} -> Services + Payments created";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial,Operational,Financial";
}

// ══════════════════════════════════════════════════
//  DEAL events (Commercial)
// ══════════════════════════════════════════════════
public record DealCreatedEvent(string DealId, string LeadId) : DomainEvent
{
    public override string Description => $"Deal {DealId} created for Lead {LeadId}";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial,Marketing";
}

public record DealQualifiedEvent(string DealId) : DomainEvent
{
    public override string Description => $"Deal {DealId} qualified";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial";
}

public record DealConvertedEvent(string DealId, string LeadId) : DomainEvent
{
    public override string Description => $"Deal {DealId} converted -> Lead {LeadId} is now a customer";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial,Identity,Marketing";
}

public record DealActivatedEvent(string DealId) : DomainEvent
{
    public override string Description => $"Deal {DealId} activated (recurring plan active)";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial,Financial";
}

public record DealConcludedEvent(string DealId, string LeadId) : DomainEvent
{
    public override string Description => $"Deal {DealId} concluded";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial,Marketing,Happiness";
}

public record DealDiscardedEvent(string DealId) : DomainEvent
{
    public override string Description => $"Deal {DealId} discarded -> archive all initial orders";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Commercial";
}

// ══════════════════════════════════════════════════
//  LEAD events (Commercial)
// ══════════════════════════════════════════════════
public record LeadCreatedEvent(string LeadId) : DomainEvent
{
    public override string Description => $"Lead {LeadId} created";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Identity,Marketing";
}

public record LeadConvertedEvent(string LeadId) : DomainEvent
{
    public override string Description => $"Lead {LeadId} converted to customer";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Financial,Identity";
}

public record LeadStatusChangedEvent(string LeadId, string NewStatus) : DomainEvent
{
    public override string Description => $"Lead {LeadId} status -> {NewStatus}";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Marketing";
}

// ══════════════════════════════════════════════════
//  SERVICEBOOKED events (Commercial -> Operational)
// ══════════════════════════════════════════════════

/// <summary>Commercial -> Operational: offerta accettata, crea WorkOrder in Completing.</summary>
public record ServizioVendutoEvent(string ServiceBookedId, string DealId, string LeadId, string ServiceType, string? Address, decimal EstimatedVolume, string? ContactName) : DomainEvent
{
    public override string Description => $"ServiceBooked {ServiceBookedId} venduto -> Operational crea WorkOrder in Completing ({ServiceType})";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: ServiceBooked Ready, WO passa Completing -> ToSchedule.</summary>
public record ServizioProntoEvent(string ServiceBookedId, string WorkOrderId) : DomainEvent
{
    public override string Description => $"ServiceBooked {ServiceBookedId} pronto -> WorkOrder {WorkOrderId} ToSchedule";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: richiesta sopralluogo per un ServiceBooked.</summary>
public record SopralluogoRichiestoEvent(string ServiceBookedId, string? QuestionnaireId, string? Address) : DomainEvent
{
    public override string Description => $"Sopralluogo richiesto per ServiceBooked {ServiceBookedId}";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: intervento risolto (riprogramma/riprendi/chiudi).</summary>
public record InterventoRisoltoEvent(string WorkOrderId, string Azione, string Note) : DomainEvent
{
    public override string Description => $"Intervento risolto su WO {WorkOrderId}: {Azione} — {Note}";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: dati venduti aggiornati (snapshot rinfrescato su WO).</summary>
public record DatiVenditaAggiornatiEvent(string ServiceBookedId, string WorkOrderId, string CampiModificati) : DomainEvent
{
    public override string Description => $"Dati vendita aggiornati su ServiceBooked {ServiceBookedId} -> WO {WorkOrderId} ({CampiModificati})";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: chiusura anticipata (WO InExecution -> ToVerify senza completamento esecuzione).</summary>
public record ChiusuraAnticipataEvent(string WorkOrderId, string? ServiceBookedId, string Motivo) : DomainEvent
{
    public override string Description => $"Chiusura anticipata WO {WorkOrderId}: {Motivo}";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

/// <summary>Commercial -> Operational: servizio cancellato.</summary>
public record ServizioCancellatoEvent(string ServiceBookedId, string? WorkOrderId) : DomainEvent
{
    public override string Description => $"Servizio {ServiceBookedId} cancellato";
    public override string SourceBC => "Commercial";
    public override string TargetBC => "Operational";
}

// ══════════════════════════════════════════════════
//  WORKORDER events (Operational -> Commercial)
// ══════════════════════════════════════════════════

/// <summary>Operational -> Commercial: WorkOrder concluso, risultati disponibili.</summary>
public record ServizioCompletatoEvent(string WorkOrderId, string? ServiceBookedId, decimal ActualVolume, bool HasDifferences, string? Notes) : DomainEvent
{
    public override string Description => $"WorkOrder {WorkOrderId} completato -> ServiceBooked {ServiceBookedId} (diff: {HasDifferences})";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Commercial";
}

/// <summary>Operational -> Commercial: Ops ha bisogno di decisione commerciale.</summary>
public record RichiedeInterventoEvent(string WorkOrderId, string? ServiceBookedId, string Motivo) : DomainEvent
{
    public override string Description => $"WorkOrder {WorkOrderId} richiede intervento: {Motivo}";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Commercial";
}

/// <summary>Operational -> Commercial: sopralluogo eseguito (da AR Inspection, non WorkOrder).</summary>
public record InspectionCompletataEvent(string InspectionId, string? ServiceBookedId, string? QuestionnaireId) : DomainEvent
{
    public override string Description => $"Inspection {InspectionId} completata -> ServiceBooked {ServiceBookedId}";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Commercial";
}

/// <summary>Operational -> Commercial: Mission confermata, orario pianificato comunicato a Commercial.</summary>
public record OrarioPianificatoConfermatoEvent(string WorkOrderId, string MissionId, DateTime OrarioPianificato) : DomainEvent
{
    public override string Description => $"Orario confermato per WO {WorkOrderId} (Mission {MissionId}): {OrarioPianificato:yyyy-MM-dd HH:mm}";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Commercial";
}

/// <summary>Operational: WorkOrder creato.</summary>
public record WorkOrderCreatedEvent(string WorkOrderId, string WorkOrderType, string ServiceType) : DomainEvent
{
    public override string Description => $"WorkOrder {WorkOrderId} created (type: {WorkOrderType}, service: {ServiceType})";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Operational";
}

/// <summary>Operational: stato WorkOrder cambiato.</summary>
public record WorkOrderStatusChangedEvent(string WorkOrderId, string OldStatus, string NewStatus, string Reason) : DomainEvent
{
    public override string Description => $"WorkOrder {WorkOrderId}: {OldStatus} -> {NewStatus} ({Reason})";
    public override string SourceBC => "Operational";
    public override string TargetBC => "Operational";
}

// ══════════════════════════════════════════════════
//  EXECUTION events (Execution -> Operational)
// ══════════════════════════════════════════════════
public record ShiftCreatedEvent(string ShiftId, string? MissionId) : DomainEvent
{
    public override string Description => $"Shift {ShiftId} created for Mission {MissionId ?? "unplanned"}";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Execution";
}

/// <summary>Execution -> Operational: operazione avviata (Shift avviato).</summary>
public record OperationStartedEvent(string ShiftId, string? WorkOrderId) : DomainEvent
{
    public override string Description => $"Shift {ShiftId} avviato (WO: {WorkOrderId ?? "N/A"})";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Operational";
}

/// <summary>Execution -> Operational: operazione completata (Shift completato).</summary>
public record OperationCompletedEvent(string ShiftId, string? WorkOrderId) : DomainEvent
{
    public override string Description => $"Shift {ShiftId} completato (WO: {WorkOrderId ?? "N/A"})";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Operational";
}

/// <summary>Execution -> Operational: operazione interrotta.</summary>
public record OperationInterruptedEvent(string ShiftId, string? WorkOrderId, string Motivo) : DomainEvent
{
    public override string Description => $"Shift {ShiftId} interrotto (WO: {WorkOrderId ?? "N/A"}): {Motivo}";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Operational";
}

public record ServiceExecutedEvent(string ShiftId, string ServiceEntryId, string ServiceId, string Outcome) : DomainEvent
{
    public override string Description => $"Service {ServiceId} executed in Shift {ShiftId} -> {Outcome}";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Operational,Commercial";
}

public record ObjectStateChangedEvent(string ObjectId, string OldState, string NewState, string OperationType) : DomainEvent
{
    public override string Description => $"Object {ObjectId}: {OldState} -> {NewState} (via {OperationType})";
    public override string SourceBC => "Execution";
    public override string TargetBC => "Execution";
}

// ══════════════════════════════════════════════════
//  FINANCIAL events
// ══════════════════════════════════════════════════
public record PaymentCreatedEvent(string PaymentId, string DealId) : DomainEvent
{
    public override string Description => $"Payment {PaymentId} created for Deal {DealId}";
    public override string SourceBC => "Financial";
    public override string TargetBC => "Financial";
}

public record ChargeExecutedEvent(string PaymentId, string ChargeId, decimal Amount) : DomainEvent
{
    public override string Description => $"Charge {ChargeId} of {Amount:C} executed on Payment {PaymentId}";
    public override string SourceBC => "Financial";
    public override string TargetBC => "Financial";
}

// ══════════════════════════════════════════════════
//  IDENTITY / MARKETING / HAPPINESS events
// ══════════════════════════════════════════════════
public record UserCreatedEvent(string UserId, string Role) : DomainEvent
{
    public override string Description => $"User {UserId} created with role {Role}";
    public override string SourceBC => "Identity";
    public override string TargetBC => "Identity";
}

public record FunnelStepAdvancedEvent(string ClientId, string NewStep) : DomainEvent
{
    public override string Description => $"Marketing client {ClientId} -> funnel step {NewStep}";
    public override string SourceBC => "Marketing";
    public override string TargetBC => "Marketing";
}

public record SatisfactionRecordedEvent(string ClientId, string ServiceId, int Score) : DomainEvent
{
    public override string Description => $"Happiness: client {ClientId}, service {ServiceId}, score {Score}/5";
    public override string SourceBC => "Happiness";
    public override string TargetBC => "Happiness";
}

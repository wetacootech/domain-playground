namespace WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

public record ClientData(string ClientName, string Phone, string? Signature = null) : ValueObject;
public record InspectionData(string? InspectionId, string? QuestionnaireId) : ValueObject;

/// <summary>
/// Snapshot immutabile della lista oggetti stimati del ServiceBooked venduto (DDD5 §2.1, review 2026-04-17).
/// Copia di ServiceBookedItem dal lato Commercial, congelata alla creazione dello Shift.
/// Serve da riferimento per l'operatore in sito ("questi sono gli oggetti dichiarati in vendita").
/// </summary>
public record ReferenceItem(string ObjectTemplateId, string TemplateName, int Quantity, decimal UnitVolume) : ValueObject
{
    public decimal TotalVolume => Quantity * UnitVolume;
}

/// <summary>
/// ServiceEntry — scheda operativa per un WorkOrder all'interno di uno Shift (DDD5 §5.1, review 2026-04-14).
/// Nessuna state machine: solo flag <see cref="Completed"/> + <see cref="CompletedAt"/>.
/// Gli esiti "parziale / interrotto / fallito" vivono nel payload di chiusura dello Shift (Sospesa),
/// non su questa entity. La cancellazione nasce dal WorkOrder (Annullato): il client filtra.
/// </summary>
public class ServiceEntry : Entity
{
    public ServiceEntry() { Id = NextEntityId("se"); }
    public string ServiceId { get; set; } = "";
    public string? DealId { get; set; }
    public string? LeadId { get; set; }
    public ServiceEntryType Type { get; set; }
    public ClientData? ClientInfo { get; set; }
    public InspectionData? Inspection { get; set; }
    /// <summary>Snapshot della lista oggetti stimati dal ServiceBooked venduto. Riferimento read-only per operatore.</summary>
    public List<ReferenceItem> ReferenceItems { get; set; } = [];
    /// <summary>Volume stimato congelato dal ServiceBooked al momento della creazione dello Shift.</summary>
    public decimal ReferenceVolume { get; set; }

    /// <summary>L'operatore dichiara "fatto" per questo Service. Immutabile una volta true.</summary>
    public bool Completed { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public void Complete(string? signature = null)
    {
        if (Completed) return;
        Completed = true;
        CompletedAt = DateTime.UtcNow;
        if (signature != null && ClientInfo != null)
            ClientInfo = ClientInfo with { Signature = signature };
    }
}

/// <summary>
/// Outcome di un ServiceEntry non completato al momento della chiusura Shift (Sospesa).
/// Vive come payload, non come stato di ServiceEntry.
/// </summary>
public record ServiceEntryOutcome(string ServiceEntryId, string Outcome, string? Reason = null, decimal? ResidualVolume = null) : ValueObject;

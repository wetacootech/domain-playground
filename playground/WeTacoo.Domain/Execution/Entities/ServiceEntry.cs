namespace WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

public record ClientData(string ClientName, string Phone, string? Signature = null) : ValueObject;
public record InspectionData(string? InspectionId, string? QuestionnaireId) : ValueObject;

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

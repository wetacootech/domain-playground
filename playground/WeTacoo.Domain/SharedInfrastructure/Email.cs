namespace WeTacoo.Domain.SharedInfrastructure;
using WeTacoo.Domain.Common;

/// <summary>
/// Email (DDD5 §9.1 Communications). AR outbox per storicizzare e inviare email.
/// References permette di collegare l'email ad entità cross-BC (Lead, Deal, Quotation, Service, Plan).
/// </summary>
public record EmailReferences(
    string? LeadId = null,
    string? DealId = null,
    string? QuotationId = null,
    string? CommercialServiceId = null,
    string? OperationalServiceId = null,
    string? PlanId = null) : ValueObject;

public class Email : AggregateRoot
{
    public Email() { Id = NextId("mail"); }
    public string To { get; set; } = "";
    public string? From { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Type { get; set; } = "Transactional"; // Transactional, Marketing, Notification
    public EmailReferences References { get; set; } = new();
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    public void MarkSent() { Status = "Sent"; SentAt = DateTime.UtcNow; Touch(); }
    public void MarkFailed(string error) { Status = "Failed"; ErrorMessage = error; Touch(); }
}

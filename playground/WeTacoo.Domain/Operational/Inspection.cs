namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

/// <summary>
/// Inspection (Sopralluogo) — AR separato in Operational (DDD5 §4.8, review 2026-04-13).
/// Non genera Mission ne' Shift. Non coinvolge Execution.
/// Ciclo di vita: Da completare -> Completato / Annullato.
/// </summary>
public class Inspection : AggregateRoot
{
    public Inspection() { Id = NextId("insp"); }
    public string? ServiceBookedId { get; set; }
    public string? QuestionnaireId { get; set; }
    public string? Caratteristiche { get; set; }
    public DateTime? DataRichiesta { get; set; }
    public List<string> Documenti { get; set; } = [];
    public bool IsCompleted { get; private set; }
    public bool IsCancelled { get; private set; }
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string Status => IsCancelled ? "Annullato" : IsCompleted ? "Completato" : "Da completare";

    public void Complete(string by)
    {
        if (IsCancelled) return;
        IsCompleted = true;
        CompletedBy = by;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        IsCancelled = true;
        Touch();
    }
}

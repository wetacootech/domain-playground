namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

/// <summary>
/// Pallet (DDD5 §5.6). AR contenitore di PhysicalObject. Ha Position e History proprio.
/// </summary>
public class Pallet : AggregateRoot
{
    public Pallet() { Id = NextId("pallet"); }
    public string Name { get; set; } = "";
    public string? LabelId { get; set; }
    public string? DealId { get; set; }
    public string? LeadId { get; set; }
    public ObjectPosition? Position { get; set; }
    public ObjectStatus Status { get; set; } = ObjectStatus.Draft;
    public List<ObjectSnapshot> History { get; set; } = [];
    public List<string> ObjectIds { get; set; } = [];
    public List<string> Documenti { get; set; } = [];
}

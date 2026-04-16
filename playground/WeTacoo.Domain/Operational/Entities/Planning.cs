namespace WeTacoo.Domain.Operational.Entities;
using WeTacoo.Domain.Common;

public record ServiceRef(string ServiceId, int VolumePercentage, bool VolumeOverride = false) : ValueObject;

public class Mission : Entity
{
    public Mission() { Id = NextEntityId("miss"); }
    public string? TeamId { get; set; }
    public List<ServiceRef> ServiceRefs { get; set; } = [];
    public List<string> VehicleResourceIds { get; set; } = [];
    public List<string> AssetResourceIds { get; set; } = [];
    public string? TimeSlot { get; set; }
    public string? Notes { get; set; }
    public bool IsCancelled { get; set; }
}

public class PlanningTeam : Entity
{
    public PlanningTeam() { Id = NextEntityId("team"); }
    public List<string> OperatorIds { get; set; } = [];
    public string? Notes { get; set; }
}

public class Resource : Entity
{
    public Resource() { Id = NextEntityId("res"); }
    public string ResourceType { get; set; } = "operator"; // operator | vehicle | asset
    public string SourceId { get; set; } = "";
    public string? AreaId { get; set; }
    public string? AvailabilitySlot { get; set; }
    public string? Notes { get; set; }
}

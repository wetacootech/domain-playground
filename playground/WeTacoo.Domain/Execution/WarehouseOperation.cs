namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

public class WarehouseOperation : AggregateRoot
{
    public WarehouseOperation() { Id = NextId("whop"); }
    public string WarehouseId { get; set; } = "";
    public string? MissionId { get; set; }
    public string? VehicleId { get; set; }
    public string OperationType { get; set; } = "IN"; // IN | OUT
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<string> ObjectIds { get; set; } = [];
    public string Status { get; set; } = "Pending";

    public void Start() { Status = "InProgress"; StartTime = DateTime.UtcNow; Touch(); }
    public void Complete() { Status = "Completed"; EndTime = DateTime.UtcNow; Touch(); }
}

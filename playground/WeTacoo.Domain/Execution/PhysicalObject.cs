namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

public record ObjectPosition(string? WarehouseId, string? Position, string? Container) : ValueObject;
public record ObjectSnapshot(ObjectStatus Status, ObjectPosition? Position, DateTime Timestamp, string? MissionId, string? Notes) : ValueObject;

public class PhysicalObject : AggregateRoot
{
    public PhysicalObject() { Id = NextId("obj"); }
    public string? LabelId { get; set; }
    public string? GroupId { get; set; }
    public string? PalletId { get; set; }
    public string? LeadId { get; set; }
    public string? DealId { get; set; }
    public string Name { get; set; } = "";
    public decimal Volume { get; set; }
    public ObjectStatus Status { get; private set; } = ObjectStatus.Draft;
    public ObjectPosition? Position { get; set; }
    public List<ObjectSnapshot> History { get; private set; } = [];
    public string? ReservedByQuotationId { get; set; }
    public bool IsReported { get; set; }
    public string? ReportedReason { get; set; }

    private void SetStatus(ObjectStatus newStatus, string? missionId = null, string? notes = null)
    {
        History.Add(new(Status, Position, DateTime.UtcNow, missionId, notes));
        Status = newStatus;
        Touch();
    }

    public void PickUp(string? missionId = null) { if (Status == ObjectStatus.Draft) SetStatus(ObjectStatus.PickedUp, missionId, "Labeled/picked up"); }
    public void LoadOnVehicle(string? missionId = null) { if (Status == ObjectStatus.PickedUp) SetStatus(ObjectStatus.OnVehicle, missionId, "Loaded on vehicle"); }
    public void UnloadToWarehouse(string warehouseId, string? missionId = null)
    {
        if (Status == ObjectStatus.OnVehicle)
        {
            Position = new ObjectPosition(warehouseId, null, null);
            SetStatus(ObjectStatus.OnWarehouse, missionId, $"Unloaded to warehouse {warehouseId}");
        }
    }
    public void LoadFromWarehouse(string? missionId = null) { if (Status == ObjectStatus.OnWarehouse) SetStatus(ObjectStatus.OnVehicle, missionId, "Loaded from warehouse"); }
    public void Deliver(string? missionId = null) { if (Status is ObjectStatus.OnVehicle or ObjectStatus.OnWarehouse) SetStatus(ObjectStatus.Delivered, missionId, "Delivered to client"); }
    public void Dispose(string? missionId = null) { if (Status is ObjectStatus.OnVehicle or ObjectStatus.OnWarehouse) SetStatus(ObjectStatus.Disposed, missionId, "Disposed"); }
    public void StockDirectly(string warehouseId, string? missionId = null)
    {
        if (Status == ObjectStatus.Draft)
        {
            Position = new ObjectPosition(warehouseId, null, null);
            SetStatus(ObjectStatus.OnWarehouse, missionId, "Directly stocked (self-service)");
        }
    }
}

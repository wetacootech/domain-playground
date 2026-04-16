namespace WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Common;

public class ActivePlan : Entity
{
    public ActivePlan() { Id = NextEntityId("aplan"); }
    public string QuotationId { get; set; } = "";
    public decimal MonthlyFee { get; set; }
    public decimal CurrentM3 { get; set; }
    public string? AreaId { get; set; }
    public int ObjectCount { get; set; }
    public List<string> ObjectIds { get; set; } = [];
    public List<string> History { get; set; } = [];

    public void UpdateAfterPartialDelivery(int removedObjects, decimal removedM3)
    {
        ObjectCount -= removedObjects;
        CurrentM3 -= removedM3;
        History.Add($"{DateTime.UtcNow:u} Partial delivery: -{removedObjects} objects, -{removedM3:F1} m³");
    }

    public void UpdateAfterPartialPickup(int addedObjects, decimal addedM3)
    {
        ObjectCount += addedObjects;
        CurrentM3 += addedM3;
        History.Add($"{DateTime.UtcNow:u} Partial pickup: +{addedObjects} objects, +{addedM3:F1} m³");
    }
}

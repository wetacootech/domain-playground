namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;

/// <summary>
/// VehicleOperation (DDD5 §5.3). AR per operazioni sul veicolo: CheckIn, Manutenzione, Loading, Unloading.
/// Separata da WarehouseOperation perché i tempi e le risorse sono diversi.
/// </summary>
public class VehicleOperation : AggregateRoot
{
    public VehicleOperation() { Id = NextId("vop"); }
    public string VehicleId { get; set; } = "";
    public string Type { get; set; } = "CheckIn"; // CheckIn, Manutenzione, Loading, Unloading
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public List<string> OperatorIds { get; set; } = [];
    public string Status { get; set; } = "Created"; // Created, InProgress, Completed, Failed
    public string? Notes { get; set; }

    public void Start() { if (Status == "Created") { Status = "InProgress"; StartTime = DateTime.UtcNow; Touch(); } }
    public void Complete() { if (Status == "InProgress") { Status = "Completed"; EndTime = DateTime.UtcNow; Touch(); } }
}

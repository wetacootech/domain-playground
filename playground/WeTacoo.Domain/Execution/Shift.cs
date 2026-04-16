namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Execution.Enums;

public record MissionData(List<string> Operators, List<string> Vehicles, List<string> Assets, string? TimeSlot) : ValueObject;
public record ShiftResources(List<string> PresentOperators, List<string> PresentVehicles, List<string> PresentAssets) : ValueObject;

public class Shift : AggregateRoot
{
    public Shift() { Id = NextId("shift"); }
    public string? MissionId { get; set; }
    public bool IsAutonomous { get; set; }
    public DateTime Date { get; set; }
    /// <summary>
    /// States from DDD5 §10c (Operation): InProgress, Paused, Suspended, Completed
    /// Plus "Created" as initial pre-start state.
    /// </summary>
    public string Status { get; set; } = "Created";
    public List<OperationalTask> Tasks { get; set; } = [];
    public List<ServiceEntry> ServiceEntries { get; set; } = [];
    public MissionData? Mission { get; set; }
    public ShiftResources? Resources { get; set; }
    public List<string> Problems { get; set; } = [];

    public ServiceEntry AddServiceEntry(string serviceId, string? dealId, string? leadId, ServiceEntryType type, ClientData? clientData)
    {
        var entry = new ServiceEntry { ServiceId = serviceId, DealId = dealId, LeadId = leadId, Type = type, ClientInfo = clientData };
        ServiceEntries.Add(entry);
        Touch();
        return entry;
    }

    public OperationalTask AddTask(TaskType type, string? serviceEntryId, List<string>? objectIds = null)
    {
        var task = new OperationalTask { Type = type, ServiceEntryId = serviceEntryId, StartTime = DateTime.UtcNow, ObjectIds = objectIds ?? [] };
        Tasks.Add(task);
        Touch();
        return task;
    }

    public void Start() { Status = "InProgress"; Touch(); }
    public void Pause() { if (Status == "InProgress") { Status = "Paused"; Touch(); } }
    public void Resume() { if (Status == "Paused") { Status = "InProgress"; Touch(); } }
    public void Suspend() { if (Status == "InProgress") { Status = "Suspended"; Touch(); } }
    public void Restart() { if (Status == "Suspended") { Status = "InProgress"; Touch(); } }
    public void Complete() { Status = "Completed"; Touch(); }
}

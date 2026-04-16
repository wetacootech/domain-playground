namespace WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Execution.Enums;

public class OperationalTask : Entity
{
    public OperationalTask() { Id = NextEntityId("task"); }
    public TaskType Type { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsExtra { get; set; }
    public List<string> ObjectIds { get; set; } = [];
    public string? ServiceEntryId { get; set; }
    public string? Notes { get; set; }
}

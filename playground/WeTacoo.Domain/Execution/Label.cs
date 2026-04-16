namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;

public class Label : AggregateRoot
{
    public Label() { Id = NextId("label"); }
    public string Code { get; set; } = "";
    public string? City { get; set; }
    public string Status { get; set; } = "Created";
    public DateTime? PrintedAt { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsReferral { get; set; }
}

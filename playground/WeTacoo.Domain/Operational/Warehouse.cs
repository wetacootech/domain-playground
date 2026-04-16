namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

public class Warehouse : AggregateRoot
{
    public Warehouse() { Id = NextId("wh"); }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? AreaId { get; set; }
    public string Status { get; set; } = "Active";
    public decimal Capacity { get; set; }
}

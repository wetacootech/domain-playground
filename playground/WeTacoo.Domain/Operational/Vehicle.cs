namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

public class Vehicle : AggregateRoot
{
    public Vehicle() { Id = NextId("veh"); }
    public string Name { get; set; } = "";
    public string Plate { get; set; } = "";
    public string? AreaId { get; set; }
    public string Status { get; set; } = "Available";
    public decimal Capacity { get; set; }
}

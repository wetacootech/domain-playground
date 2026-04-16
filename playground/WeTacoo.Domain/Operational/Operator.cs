namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

public class Operator : AggregateRoot
{
    public Operator() { Id = NextId("op"); }
    public string IdentityId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public string? AreaId { get; set; }
    public string Status { get; set; } = "Active";
    public List<string> AssignedWarehouseIds { get; set; } = [];
}

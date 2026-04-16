namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;

public class Salesman : AggregateRoot
{
    public Salesman() { Id = NextId("sales"); }
    public string IdentityId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public string? HubspotId { get; set; }
    public bool IsActive { get; set; } = true;
}

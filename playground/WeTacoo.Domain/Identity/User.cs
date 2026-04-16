namespace WeTacoo.Domain.Identity;
using WeTacoo.Domain.Common;

public class User : AggregateRoot
{
    public User() { Id = NextId("user"); }
    public string Email { get; set; } = "";
    public string Username => Email;
    public string Role { get; set; } = "Customer";
    public List<string> Claims { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }
}

public class AdminUser : User
{
    public AdminUser() { Role = "Admin"; }
}

public class OperatorUser : User
{
    public OperatorUser() { Role = "Operator"; }
    public List<string> Certifications { get; set; } = [];
    public List<string> AssignedWarehouseIds { get; set; } = [];
}

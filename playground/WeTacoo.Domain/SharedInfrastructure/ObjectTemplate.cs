namespace WeTacoo.Domain.SharedInfrastructure;
using WeTacoo.Domain.Common;

public class ObjectTemplate : AggregateRoot
{
    public ObjectTemplate() { Id = NextId("otpl"); }
    public string Name { get; set; } = "";
    public string ObjectType { get; set; } = "mobile"; // mobile | oggetto
    public string? Room { get; set; }
    public decimal DefaultVolume { get; set; }
}

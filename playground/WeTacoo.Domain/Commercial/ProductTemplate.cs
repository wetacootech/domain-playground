namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;

public class ProductTemplate : AggregateRoot
{
    public ProductTemplate() { Id = NextId("prod"); }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal BasePrice { get; set; }
    public string ProductType { get; set; } = "oneoff"; // oneoff | recurring
    public string? AreaId { get; set; }
    public bool IsActive { get; set; } = true;
}

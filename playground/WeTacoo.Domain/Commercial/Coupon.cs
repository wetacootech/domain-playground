namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;

public class Coupon : AggregateRoot
{
    public Coupon() { Id = NextId("coup"); }
    public string Code { get; set; } = "";
    public decimal DiscountPercent { get; set; }
    public decimal? DiscountFixed { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

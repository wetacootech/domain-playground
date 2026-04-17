namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

public class Slot : AggregateRoot
{
    public Slot() { Id = NextId("slot"); }
    public DateTime Date { get; set; }
    public string AreaId { get; set; } = "";
    public string WarehouseId { get; set; } = "";
    public decimal MaxVolume { get; set; }
    public decimal UsedVolume { get; set; }
    public int MaxServices { get; set; }
    public int BookedServices { get; set; }

    public bool CanBook(decimal volume) => UsedVolume + volume <= MaxVolume && BookedServices < MaxServices;

    public void Book(decimal volume)
    {
        UsedVolume += volume;
        BookedServices++;
        Touch();
    }
}

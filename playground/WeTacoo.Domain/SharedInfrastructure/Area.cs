namespace WeTacoo.Domain.SharedInfrastructure;
using WeTacoo.Domain.Common;

public class Area : AggregateRoot
{
    public Area() { Id = NextId("area"); }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public List<string> ZipCodes { get; set; } = [];
    public int MinBookingDays { get; set; } = 3;
    public int MinDeliveryDays { get; set; } = 5;
}

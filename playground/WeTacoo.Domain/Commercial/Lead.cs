namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.ValueObjects;

public class Lead : AggregateRoot
{
    public Lead() { Id = NextId("lead"); }
    public Personal Personal { get; set; } = new("", "", "", "");
    public LeadStatus Status { get; private set; } = LeadStatus.ToConvert;
    public bool IsReturning { get; private set; }
    public CustomerData Customer { get; set; } = new(false);
    public string? IdentityId { get; set; }
    public string? FinancialClientId { get; set; }
    public string? HubspotLeadKey { get; set; }
    public List<string> DealIds { get; private set; } = [];

    public void RecalculateStatus(IEnumerable<DealStatus> dealStatuses)
    {
        Touch();
        var statuses = dealStatuses.ToList();
        if (statuses.Count == 0)
        {
            Status = LeadStatus.ToConvert;
            return;
        }
        bool hasActive = statuses.Any(s => s is not (DealStatus.Concluded or DealStatus.NotConverted or DealStatus.Cancelled));
        if (hasActive)
        {
            Status = LeadStatus.Converted;
            return;
        }
        Status = LeadStatus.Concluded;
    }

    public void MarkConverted()
    {
        Status = LeadStatus.Converted;
        Customer = new CustomerData(true, Id);
        Touch();
    }

    public void AddDeal(string dealId)
    {
        DealIds.Add(dealId);
        if (Status == LeadStatus.Concluded)
            IsReturning = true;
        Touch();
    }
}

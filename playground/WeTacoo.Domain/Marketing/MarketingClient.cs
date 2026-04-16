namespace WeTacoo.Domain.Marketing;
using WeTacoo.Domain.Common;

public class MarketingDeal : Entity
{
    public MarketingDeal() { Id = NextEntityId("mdeal"); }
    public string DealId { get; set; } = "";
    public string? QuotationId { get; set; }
    public string FunnelQuotationStep { get; set; } = "New";
}

public class MarketingClient : AggregateRoot
{
    public MarketingClient() { Id = NextId("mkt"); }
    public string CommercialLeadId { get; set; } = "";
    public string FunnelStep { get; set; } = "New";
    public List<MarketingDeal> Deals { get; set; } = [];

    public void AdvanceFunnel(string step) { FunnelStep = step; Touch(); }
    public void AddDeal(string dealId) { Deals.Add(new MarketingDeal { DealId = dealId }); Touch(); }
}

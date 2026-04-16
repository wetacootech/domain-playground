namespace WeTacoo.Domain.Financial;
using WeTacoo.Domain.Common;

public class FinancialClient : AggregateRoot
{
    public FinancialClient() { Id = NextId("fcli"); }
    public string CommercialLeadId { get; set; } = "";
    public string? StripeId { get; set; }
    public string PaymentHealth { get; set; } = "Good";
    public decimal Credit { get; set; }
    public string? BillingName { get; set; }
    public string? BillingAddress { get; set; }
    public string? VatNumber { get; set; }
    public string? Notes { get; set; }
}

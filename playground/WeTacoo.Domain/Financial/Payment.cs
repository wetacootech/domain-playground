namespace WeTacoo.Domain.Financial;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Financial.Enums;

public class Charge : Entity
{
    public Charge() { Id = NextEntityId("chrg"); }
    public decimal Amount { get; set; }
    public ChargeStatus Status { get; set; } = ChargeStatus.Pending;
    public DateTime? DueDate { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? StripePaymentId { get; set; }
    public string? Notes { get; set; }

    public void Execute() { Status = ChargeStatus.Executed; ExecutedAt = DateTime.UtcNow; }
    public void Fail() => Status = ChargeStatus.Failed;
    public void Postpone(DateTime newDate) { Status = ChargeStatus.Postponed; DueDate = newDate; }
}

public class SimplifiedProduct : Entity
{
    public SimplifiedProduct() { Id = NextEntityId("sprod"); }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
}

public class Payment : AggregateRoot
{
    public Payment() { Id = NextId("pay"); }
    public string ClientId { get; set; } = "";
    public string DealId { get; set; } = "";
    public string? QuotationId { get; set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string PaymentType { get; set; } = "OneOff"; // OneOff | Recurring
    public decimal VatRate { get; set; }
    public List<Charge> Charges { get; set; } = [];
    public List<SimplifiedProduct> Products { get; set; } = [];
    public decimal TotalAmount => Products.Sum(p => p.Price);
    public decimal PaidAmount => Charges.Where(c => c.Status == ChargeStatus.Executed).Sum(c => c.Amount);

    public Charge AddCharge(decimal amount, DateTime? dueDate = null)
    {
        var charge = new Charge { Amount = amount, DueDate = dueDate ?? DateTime.UtcNow };
        Charges.Add(charge);
        UpdateStatus();
        Touch();
        return charge;
    }

    public void ExecuteCharge(string chargeId)
    {
        var charge = Charges.FirstOrDefault(c => c.Id == chargeId);
        charge?.Execute();
        UpdateStatus();
        Touch();
    }

    private void UpdateStatus()
    {
        if (PaidAmount >= TotalAmount) Status = PaymentStatus.Paid;
        else if (PaidAmount > 0) Status = PaymentStatus.PartiallyPaid;
        else if (Charges.Any(c => c.Status == ChargeStatus.Failed)) Status = PaymentStatus.Failed;
    }
}

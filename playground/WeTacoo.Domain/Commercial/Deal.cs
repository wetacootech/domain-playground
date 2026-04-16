namespace WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.Entities;

public class Deal : AggregateRoot
{
    public Deal() { Id = NextId("deal"); }
    public string LeadId { get; set; } = "";
    public string? SalesmanId { get; set; }
    public DealStatus Status { get; private set; } = DealStatus.ToQualify;
    /// <summary>Derivato: Recurring se contiene almeno una Quotation accettata con DraftPlan (o se ha gia' un ActivePlan); altrimenti OneOff.</summary>
    public DealType Type => ActivePlan != null
        || Quotations.Any(q => q.IsAccepted && q.DraftPlans.Count > 0)
        ? DealType.Recurring : DealType.OneOff;
    public string? AreaId { get; set; }
    public string? HubspotId { get; set; }
    public List<Quotation> Quotations { get; set; } = [];
    public ActivePlan? ActivePlan { get; private set; }
    public List<string> StatusHistory { get; private set; } = [];

    private void SetStatus(DealStatus newStatus)
    {
        StatusHistory.Add($"{DateTime.UtcNow:u} {Status} → {newStatus}");
        Status = newStatus;
        Touch();
    }

    public void Qualify() { if (Status == DealStatus.ToQualify) SetStatus(DealStatus.Qualified); }
    public void EnterNegotiation() { if (Status == DealStatus.Qualified) SetStatus(DealStatus.InNegotiation); }
    public void ReleaseToClient() { if (Status == DealStatus.InNegotiation) SetStatus(DealStatus.Qualified); }

    public void Convert()
    {
        // DDD5 §10c: solo In trattativa → Convertito
        if (Status == DealStatus.InNegotiation)
            SetStatus(DealStatus.Converted);
    }

    /// <summary>Creates ActivePlan from DraftPlan at acceptance. Does NOT change Deal status.</summary>
    public void CreatePlan(Quotation acceptedQuotation, DraftPlan chosenPlan)
    {
        if (ActivePlan != null) return; // already has a plan
        ActivePlan = new ActivePlan
        {
            QuotationId = acceptedQuotation.Id,
            MonthlyFee = chosenPlan.MonthlyFee,
            CurrentM3 = chosenPlan.EstimatedM3,
            AreaId = chosenPlan.AreaId
        };
        Touch();
    }

    /// <summary>Transitions to Active when all services for this Deal are completed.</summary>
    public void Activate()
    {
        if (Status != DealStatus.Converted) return;
        SetStatus(DealStatus.Active);
    }

    public void Conclude()
    {
        if (Status is DealStatus.Converted or DealStatus.Active)
            SetStatus(DealStatus.Concluded);
    }

    /// <summary>Checks if Deal should close based on remaining objects and service completion. Returns true if deal was concluded.</summary>
    public bool TryCloseIfNoObjectsRemaining(int objectsOnWarehouseForDeal, bool allServicesCompleted = false)
    {
        if (Status != DealStatus.Active) return false;

        // Recurring (with ActivePlan): close when no objects in warehouse
        if (ActivePlan != null)
        {
            if (objectsOnWarehouseForDeal > 0)
            {
                ActivePlan.ObjectCount = objectsOnWarehouseForDeal;
                return false;
            }
            ActivePlan.History.Add($"{DateTime.UtcNow:u} Plan closed — 0 objects remaining");
            Conclude();
            return true;
        }

        // OneOff (no ActivePlan): close when all services completed and no objects in transit
        if (allServicesCompleted || objectsOnWarehouseForDeal == 0)
        {
            Conclude();
            return true;
        }
        return false;
    }

    public void Discard()
    {
        if (Status is DealStatus.ToQualify or DealStatus.Qualified or DealStatus.InNegotiation)
            SetStatus(DealStatus.NotConverted);
    }

    public void Cancel()
    {
        if (Status == DealStatus.Converted)
            SetStatus(DealStatus.Cancelled);
    }
}

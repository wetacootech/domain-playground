using WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.ValueObjects;
using WeTacoo.Domain.Operational;
using WeTacoo.Domain.Operational.Entities;
using WeTacoo.Domain.Operational.Enums;
using WeTacoo.Domain.Operational.ValueObjects;
using WeTacoo.Domain.Execution;
using WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Execution.Enums;
using WeTacoo.Domain.Financial;
using WeTacoo.Domain.Financial.Enums;
using WeTacoo.Domain.Events;

namespace WeTacoo.Tests;

/// <summary>
/// UC-9: Stesso cliente, due depositi attivi in due citta' diverse.
/// Verifica: due Deal distinti per lo stesso Lead, due ActivePlan indipendenti,
/// due Quotation di consegna (una per Deal) con stessa destinazione ma Deal diversi,
/// chiusura indipendente di ciascun Deal.
/// </summary>
public class UC9_DueDepositiTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_SameLead_TwoDeals_InDifferentAreas()
    {
        var lead = new Lead { Personal = new Personal("Giulia", "Neri", "giulia@test.com", "+39 333") };
        var dealMI = new Deal { LeadId = lead.Id, AreaId = "area-mi" };
        var dealRM = new Deal { LeadId = lead.Id, AreaId = "area-rm" };
        lead.AddDeal(dealMI.Id);
        lead.AddDeal(dealRM.Id);

        Assert.Equal(2, lead.DealIds.Count);
        Assert.NotEqual(dealMI.Id, dealRM.Id);
        Assert.NotEqual(dealMI.AreaId, dealRM.AreaId);
    }

    [Fact]
    public void Step2_TwoIndependentActivePlans()
    {
        var dealMI = new Deal { LeadId = "lead-1", AreaId = "area-mi" };
        dealMI.Qualify();
        dealMI.EnterNegotiation();
        dealMI.Convert();
        var qMI = new Quotation { DealId = dealMI.Id };
        qMI.DraftPlans.Add(new DraftPlan { MonthlyFee = 99m, EstimatedM3 = 20m, AreaId = "area-mi" });
        dealMI.Quotations.Add(qMI);
        dealMI.CreatePlan(qMI, qMI.DraftPlans[0]);

        var dealRM = new Deal { LeadId = "lead-1", AreaId = "area-rm" };
        dealRM.Qualify();
        dealRM.EnterNegotiation();
        dealRM.Convert();
        var qRM = new Quotation { DealId = dealRM.Id };
        qRM.DraftPlans.Add(new DraftPlan { MonthlyFee = 79m, EstimatedM3 = 15m, AreaId = "area-rm" });
        dealRM.Quotations.Add(qRM);
        dealRM.CreatePlan(qRM, qRM.DraftPlans[0]);

        Assert.NotNull(dealMI.ActivePlan);
        Assert.NotNull(dealRM.ActivePlan);
        Assert.NotEqual(dealMI.ActivePlan!.Id, dealRM.ActivePlan!.Id);
        Assert.NotEqual(dealMI.ActivePlan.MonthlyFee, dealRM.ActivePlan.MonthlyFee);
        Assert.Equal(20m, dealMI.ActivePlan.CurrentM3);
        Assert.Equal(15m, dealRM.ActivePlan.CurrentM3);
    }

    [Fact]
    public void Step3_TwoPaymentsScheduled_Independently()
    {
        var payMI = new Payment { DealId = "deal-mi", PaymentType = "Recurring" };
        payMI.Products.Add(new SimplifiedProduct { Name = "Canone MI", Price = 99m });
        var payRM = new Payment { DealId = "deal-rm", PaymentType = "Recurring" };
        payRM.Products.Add(new SimplifiedProduct { Name = "Canone RM", Price = 79m });

        Assert.NotEqual(payMI.DealId, payRM.DealId);
        Assert.Equal(99m, payMI.TotalAmount);
        Assert.Equal(79m, payRM.TotalAmount);
    }

    [Fact]
    public void Step4_ObjectsLinkedToOwnDeal()
    {
        var objsMI = Enumerable.Range(0, 20).Select(i => new PhysicalObject { Name = $"mi-{i}", DealId = "deal-mi" }).ToList();
        var objsRM = Enumerable.Range(0, 15).Select(i => new PhysicalObject { Name = $"rm-{i}", DealId = "deal-rm" }).ToList();

        Assert.All(objsMI, o => Assert.Equal("deal-mi", o.DealId));
        Assert.All(objsRM, o => Assert.Equal("deal-rm", o.DealId));
        Assert.Equal(35, objsMI.Count + objsRM.Count);
    }

    [Fact]
    public void Step5_FinalDelivery_TwoQuotations_OnePerDeal_SameDestination()
    {
        var dealMI = new Deal { LeadId = "lead-1" };
        var dealRM = new Deal { LeadId = "lead-1" };

        var addrFirenze = new Address("Via Firenze 1", "Firenze", "50100", "area-fi");

        var qDeliveryMI = new Quotation { DealId = dealMI.Id, IsInitial = false };
        qDeliveryMI.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = addrFirenze,
            SelectedObjectIds = Enumerable.Range(0, 20).Select(i => $"mi-{i}").ToList()
        });

        var qDeliveryRM = new Quotation { DealId = dealRM.Id, IsInitial = false };
        qDeliveryRM.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = addrFirenze,
            SelectedObjectIds = Enumerable.Range(0, 15).Select(i => $"rm-{i}").ToList()
        });

        Assert.NotEqual(qDeliveryMI.DealId, qDeliveryRM.DealId);
        Assert.Equal(qDeliveryMI.Services[0].ServiceAddress, qDeliveryRM.Services[0].ServiceAddress);
        Assert.Equal(20, qDeliveryMI.Services[0].SelectedObjectIds.Count);
        Assert.Equal(15, qDeliveryRM.Services[0].SelectedObjectIds.Count);
    }

    [Fact]
    public void Step6_TwoMissions_DifferentWarehousesOrigin_SameDestination()
    {
        var planning = new Planning { Date = DateTime.Today.AddDays(10) };
        var teamMI = new PlanningTeam { OperatorIds = ["op-mi-1"] };
        var teamRM = new PlanningTeam { OperatorIds = ["op-rm-1"] };
        planning.Teams.Add(teamMI);
        planning.Teams.Add(teamRM);

        var missionMI = planning.AddMission(teamMI.Id, [new ServiceRef("wo-consegna-mi", 100)], ["v-mi"]);
        var missionRM = planning.AddMission(teamRM.Id, [new ServiceRef("wo-consegna-rm", 100)], ["v-rm"]);

        Assert.NotEqual(missionMI.Id, missionRM.Id);
        Assert.NotEqual(missionMI.TeamId, missionRM.TeamId);
    }

    [Fact]
    public void Step7_TwoWarehouseOperationsOUT_OnePerWarehouse()
    {
        var whOutMI = new WarehouseOperation { WarehouseId = "wh-mi", OperationType = "OUT", MissionId = "miss-mi" };
        var whOutRM = new WarehouseOperation { WarehouseId = "wh-rm", OperationType = "OUT", MissionId = "miss-rm" };

        whOutMI.Start(); whOutMI.Complete();
        whOutRM.Start(); whOutRM.Complete();

        Assert.Equal("Completed", whOutMI.Status);
        Assert.Equal("Completed", whOutRM.Status);
        Assert.NotEqual(whOutMI.WarehouseId, whOutRM.WarehouseId);
    }

    [Fact]
    public void Step8_TwoShifts_ConvergeAtSameDestination()
    {
        var shiftMI = new Shift
        {
            MissionId = "miss-mi",
            Date = DateTime.Today.AddDays(10),
            Mission = new MissionData(["op-mi-1"], ["v-mi"], [], "09:00-14:00")
        };
        var shiftRM = new Shift
        {
            MissionId = "miss-rm",
            Date = DateTime.Today.AddDays(10),
            Mission = new MissionData(["op-rm-1"], ["v-rm"], [], "10:00-14:00")
        };

        // Stessa data, team diversi
        Assert.Equal(shiftMI.Date, shiftRM.Date);
        Assert.NotEqual(shiftMI.MissionId, shiftRM.MissionId);
    }

    [Fact]
    public void Step9_EachShiftHasOwnServiceEntry_WithOwnDealId()
    {
        var shiftMI = new Shift { Date = DateTime.Today };
        var entryMI = shiftMI.AddServiceEntry("wo-consegna-mi", "deal-mi", "lead-1",
            ServiceEntryType.Consegna, new ClientData("Giulia", "333"));

        var shiftRM = new Shift { Date = DateTime.Today };
        var entryRM = shiftRM.AddServiceEntry("wo-consegna-rm", "deal-rm", "lead-1",
            ServiceEntryType.Consegna, new ClientData("Giulia", "333"));

        Assert.Equal("deal-mi", entryMI.DealId);
        Assert.Equal("deal-rm", entryRM.DealId);
        Assert.Equal(entryMI.LeadId, entryRM.LeadId);
    }

    [Fact]
    public void Step10_ObjectsDelivered_EachOwnDeal()
    {
        var objMI = new PhysicalObject { Name = "mi-0", DealId = "deal-mi" };
        var objRM = new PhysicalObject { Name = "rm-0", DealId = "deal-rm" };
        objMI.StockDirectly("wh-mi");
        objRM.StockDirectly("wh-rm");
        objMI.LoadFromWarehouse("miss-mi");
        objRM.LoadFromWarehouse("miss-rm");
        objMI.Deliver("miss-mi");
        objRM.Deliver("miss-rm");

        Assert.Equal(ObjectStatus.Delivered, objMI.Status);
        Assert.Equal(ObjectStatus.Delivered, objRM.Status);
        Assert.Equal("deal-mi", objMI.DealId);
        Assert.Equal("deal-rm", objRM.DealId);
    }

    [Fact]
    public void Step11_DealsClose_Independently()
    {
        var dealMI = new Deal { LeadId = "lead-1" };
        dealMI.Qualify(); dealMI.EnterNegotiation(); dealMI.Convert();
        var qMI = new Quotation { DealId = dealMI.Id };
        qMI.DraftPlans.Add(new DraftPlan { MonthlyFee = 99m, EstimatedM3 = 20m });
        dealMI.Quotations.Add(qMI);
        dealMI.CreatePlan(qMI, qMI.DraftPlans[0]);
        dealMI.Activate();

        var dealRM = new Deal { LeadId = "lead-1" };
        dealRM.Qualify(); dealRM.EnterNegotiation(); dealRM.Convert();
        var qRM = new Quotation { DealId = dealRM.Id };
        qRM.DraftPlans.Add(new DraftPlan { MonthlyFee = 79m, EstimatedM3 = 15m });
        dealRM.Quotations.Add(qRM);
        dealRM.CreatePlan(qRM, qRM.DraftPlans[0]);
        dealRM.Activate();

        // Deal MI si chiude (0 oggetti) ma Deal RM ne ha ancora
        var closedMI = dealMI.TryCloseIfNoObjectsRemaining(0);
        var closedRM = dealRM.TryCloseIfNoObjectsRemaining(15);

        Assert.True(closedMI);
        Assert.False(closedRM);
        Assert.Equal(DealStatus.Concluded, dealMI.Status);
        Assert.Equal(DealStatus.Active, dealRM.Status);
    }

    [Fact]
    public void Step12_LeadTracksBothDeals()
    {
        var lead = new Lead { Personal = new Personal("Giulia", "Neri", "giulia@test.com", "+39 333") };
        var d1 = new Deal { LeadId = lead.Id };
        var d2 = new Deal { LeadId = lead.Id };
        lead.AddDeal(d1.Id);
        lead.AddDeal(d2.Id);

        Assert.Equal(2, lead.DealIds.Count);
        Assert.Contains(d1.Id, lead.DealIds);
        Assert.Contains(d2.Id, lead.DealIds);
    }

    [Fact]
    public void Step13_LeadRecalculateStatus_TwoActiveDeals_StaysConverted()
    {
        var lead = new Lead { Personal = new Personal("G", "N", "g@t.com", null!) };
        lead.MarkConverted();

        lead.RecalculateStatus([DealStatus.Active, DealStatus.Active]);
        Assert.Equal(LeadStatus.Converted, lead.Status);

        // Entrambi conclusi -> Concluded
        lead.RecalculateStatus([DealStatus.Concluded, DealStatus.Concluded]);
        Assert.Equal(LeadStatus.Concluded, lead.Status);
    }
}

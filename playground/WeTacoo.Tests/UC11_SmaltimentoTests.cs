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
/// UC-11: Smaltimento parziale + consegna da deposito attivo.
/// Verifica: due ServiceBooked distinti (Smaltimento, Consegna) nella stessa Quotation,
/// possibile Mission unica con due serviceRefs, Shift con due ServiceEntry,
/// Object status terminali diversi (Disposed vs Delivered), Plan aggiornato.
/// </summary>
public class UC11_SmaltimentoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_TwoServiceBooked_DifferentTypes_SameQuotation()
    {
        var q = new Quotation { DealId = "deal-1", IsInitial = false };
        q.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Smaltimento,
            SelectedObjectIds = ["scat1", "scat2", "scat3", "scat4", "scat5"]
        });
        q.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = new Address("20100", "area-mi"),
            SelectedObjectIds = ["divano", "sedia1", "sedia2"]
        });

        Assert.Equal(2, q.Services.Count);
        Assert.Contains(q.Services, s => s.Type == ServiceBookedType.Smaltimento);
        Assert.Contains(q.Services, s => s.Type == ServiceBookedType.Consegna);
    }

    [Fact]
    public void Step2_SingleMission_WithTwoServiceRefs()
    {
        var planning = new Planning { Date = DateTime.Today };
        var team = new PlanningTeam { OperatorIds = ["op-1", "op-2"] };
        planning.Teams.Add(team);

        var mission = planning.AddMission(team.Id,
            [new ServiceRef("wo-smaltimento", 60), new ServiceRef("wo-consegna", 40)],
            ["v-1"]);

        Assert.Single(planning.Missions);
        Assert.Equal(2, mission.ServiceRefs.Count);
    }

    [Fact]
    public void Step3_Shift_WithTwoServiceEntries_DifferentTypes()
    {
        var shift = new Shift { Date = DateTime.Today, MissionId = "miss-1" };
        var entrySmalt = shift.AddServiceEntry("wo-smaltimento", "deal-1", "lead-1",
            ServiceEntryType.Smaltimento, new ClientData("WeTacoo", "0200"));
        var entryCons = shift.AddServiceEntry("wo-consegna", "deal-1", "lead-1",
            ServiceEntryType.Consegna, new ClientData("WeTacoo", "0200"));

        Assert.Equal(2, shift.ServiceEntries.Count);
        Assert.Equal(ServiceEntryType.Smaltimento, entrySmalt.Type);
        Assert.Equal(ServiceEntryType.Consegna, entryCons.Type);
    }

    [Fact]
    public void Step4_Objects_Disposed_TerminalStatus()
    {
        var scatoloni = Enumerable.Range(0, 5).Select(i =>
        {
            var o = new PhysicalObject { Name = $"scatolone-{i}", DealId = "deal-1" };
            o.StockDirectly("wh-mi");
            o.LoadFromWarehouse("miss-1");
            return o;
        }).ToList();

        foreach (var o in scatoloni) o.Dispose("miss-1");
        Assert.All(scatoloni, o => Assert.Equal(ObjectStatus.Disposed, o.Status));
    }

    [Fact]
    public void Step5_Objects_Delivered_TerminalStatus()
    {
        var consegne = new[] { "divano", "sedia1", "sedia2" }.Select(n =>
        {
            var o = new PhysicalObject { Name = n, DealId = "deal-1" };
            o.StockDirectly("wh-mi");
            o.LoadFromWarehouse("miss-1");
            return o;
        }).ToList();

        foreach (var o in consegne) o.Deliver("miss-1");
        Assert.All(consegne, o => Assert.Equal(ObjectStatus.Delivered, o.Status));
    }

    [Fact]
    public void Step6_TerminalStatuses_AreDistinct()
    {
        var smaltito = new PhysicalObject();
        smaltito.StockDirectly("wh-mi");
        smaltito.LoadFromWarehouse("miss-1");
        smaltito.Dispose();

        var consegnato = new PhysicalObject();
        consegnato.StockDirectly("wh-mi");
        consegnato.LoadFromWarehouse("miss-1");
        consegnato.Deliver();

        Assert.NotEqual(smaltito.Status, consegnato.Status);
        Assert.Equal(ObjectStatus.Disposed, smaltito.Status);
        Assert.Equal(ObjectStatus.Delivered, consegnato.Status);
    }

    [Fact]
    public void Step7_TwoWorkOrders_BothCompleteIndependently()
    {
        var woSmalt = new WorkOrder { ServiceType = new ServiceTypeVO(ServiceTypeEnum.Smaltimento, false, false, false, "area-mi") };
        var woCons = new WorkOrder { ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, false, "area-mi") };

        foreach (var wo in new[] { woSmalt, woCons })
        {
            wo.ServizioPronto("Sales");
            wo.Programma("Ops");
            wo.AvviaEsecuzione("Ops");
            wo.CompletaEsecuzione("Ops");
            wo.VerificaEConcludi("Ops");
        }

        Assert.Equal(WorkOrderStatus.Concluded, woSmalt.Status);
        Assert.Equal(WorkOrderStatus.Concluded, woCons.Status);
    }

    [Fact]
    public void Step8_PlanAdjusted_From30To22()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify(); deal.EnterNegotiation(); deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.ActivePlan!.ObjectCount = 30;
        deal.Activate();

        // 5 smaltiti + 3 consegnati = -8 oggetti
        deal.ActivePlan.UpdateAfterPartialDelivery(8, 4m);
        Assert.Equal(22, deal.ActivePlan.ObjectCount);
        Assert.Equal(26m, deal.ActivePlan.CurrentM3);
    }

    [Fact]
    public void Step9_DealStaysActive_StillHasObjects()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify(); deal.EnterNegotiation(); deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate();

        var closed = deal.TryCloseIfNoObjectsRemaining(22);
        Assert.False(closed);
        Assert.Equal(DealStatus.Active, deal.Status);
    }

    [Fact]
    public void Step10_ServiceEntry_Smaltimento_ProperType()
    {
        var entry = new ServiceEntry { ServiceId = "wo-smalt", Type = ServiceEntryType.Smaltimento };
        Assert.False(entry.Completed);
        entry.Complete();
        Assert.True(entry.Completed);
    }

    [Fact]
    public void Step11_ServiceEntry_Consegna_ProperType()
    {
        var entry = new ServiceEntry { ServiceId = "wo-cons", Type = ServiceEntryType.Consegna };
        entry.Complete();
        Assert.Equal(ServiceEntryType.Consegna, entry.Type);
        Assert.True(entry.Completed);
    }

    [Fact]
    public void Step12_WarehouseOperationOUT_ForEightObjects()
    {
        var whOut = new WarehouseOperation
        {
            WarehouseId = "wh-mi",
            MissionId = "miss-1",
            OperationType = "OUT",
            ObjectIds = ["s1", "s2", "s3", "s4", "s5", "d1", "d2", "d3"]
        };
        whOut.Start();
        whOut.Complete();
        Assert.Equal(8, whOut.ObjectIds.Count);
        Assert.Equal("Completed", whOut.Status);
    }

    [Fact]
    public void Step13_CanonAdjustment_OnlyAtServiceComplete_NotAtWarehouseExit()
    {
        // Principio UC-11 D5: il trigger e' il completamento del Service, non l'uscita dal magazzino
        var svcSmalt = new ServiceBooked { Type = ServiceBookedType.Smaltimento };
        svcSmalt.AccettaServizio();
        svcSmalt.SegnaComePronto();
        // Qui l'oggetto esce dal magazzino ma svc e' ancora Pronto
        Assert.Equal(ServiceBookedStatus.Ready, svcSmalt.Status);

        // Solo quando il servizio e' completato...
        svcSmalt.ServizioCompletato();
        Assert.Equal(ServiceBookedStatus.Completed, svcSmalt.Status);
        // ...si puo' procedere con l'adeguamento del canone
    }

    [Fact]
    public void Step14_QuotationToAdjust_AfterCompletion()
    {
        var q = new Quotation { DealId = "deal-1", IsInitial = false };
        q.Finalize();
        q.MarkToAdjust(); // post-esecuzione, analisi volume
        q.Complete();
        Assert.Equal(QuotationStatus.Completed, q.Status);
    }
}

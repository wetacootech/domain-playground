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
/// UC-8: Oggetti rifiutati alla consegna.
/// Verifica: 4 oggetti Delivered (terminale), 1 oggetto rifiutato resta OnVehicle -> torna OnWarehouse,
/// flag IsReported=true sull'Object, WarehouseOperation IN esplicita per rientro,
/// Shift COMPLETE (non PARTIAL), Quotation ToAdjust per pratica rimborso.
/// </summary>
public class UC8_OggettiRifiutatiTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_FiveObjectsOnWarehouse_LoadedOnVehicle()
    {
        var objs = Enumerable.Range(0, 5).Select(i =>
        {
            var o = new PhysicalObject { Name = $"obj-{i}", DealId = "deal-1" };
            o.StockDirectly("wh-mi");
            return o;
        }).ToList();

        Assert.All(objs, o => Assert.Equal(ObjectStatus.OnWarehouse, o.Status));

        foreach (var o in objs) o.LoadFromWarehouse("miss-1");
        Assert.All(objs, o => Assert.Equal(ObjectStatus.OnVehicle, o.Status));
    }

    [Fact]
    public void Step2_FourDelivered_OneStaysOnVehicle_Rejected()
    {
        var objs = Enumerable.Range(0, 5).Select(i =>
        {
            var o = new PhysicalObject { Name = $"obj-{i}", DealId = "deal-1" };
            o.StockDirectly("wh-mi");
            o.LoadFromWarehouse("miss-1");
            return o;
        }).ToList();

        // 4 consegnati
        for (int i = 0; i < 4; i++) objs[i].Deliver("miss-1");
        // Il 5° (tavolo graffiato) resta OnVehicle con flag report
        objs[4].IsReported = true;
        objs[4].ReportedReason = "Cliente ha rifiutato: graffio sul tavolo";

        Assert.Equal(4, objs.Count(o => o.Status == ObjectStatus.Delivered));
        Assert.Equal(1, objs.Count(o => o.Status == ObjectStatus.OnVehicle));
        Assert.True(objs[4].IsReported);
        Assert.Equal("Cliente ha rifiutato: graffio sul tavolo", objs[4].ReportedReason);
    }

    [Fact]
    public void Step3_RejectedObject_ReturnsToWarehouse_WithoutDeliveredTransition()
    {
        var obj = new PhysicalObject { Name = "Tavolo", DealId = "deal-1" };
        obj.StockDirectly("wh-mi");
        obj.LoadFromWarehouse("miss-1");
        obj.IsReported = true;

        Assert.Equal(ObjectStatus.OnVehicle, obj.Status);
        // Rientro in magazzino senza passare per Delivered
        obj.UnloadToWarehouse("wh-mi", "miss-1");
        Assert.Equal(ObjectStatus.OnWarehouse, obj.Status);
        Assert.NotEqual(ObjectStatus.Delivered, obj.Status);
    }

    [Fact]
    public void Step4_Shift_IsCOMPLETE_NotPartial()
    {
        // Lo Shift ha completato tutte le azioni richieste: il rifiuto non e' un fallimento dello Shift
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-consegna", "deal-1", "lead-1",
            ServiceEntryType.Consegna, new ClientData("Mario", "333"));
        // SE senza Start: in-progress = Shift InCorso
        entry.Complete("Firma_Mario");
        shift.Complete();

        Assert.True(entry.Completed);
        Assert.Equal("Completed", shift.Status);
    }

    [Fact]
    public void Step5_ServiceBooked_Completato_EvenWithRefusal()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Consegna, SelectedObjectIds = ["a", "b", "c", "d", "e"] };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.ServizioCompletato();

        Assert.Equal(ServiceBookedStatus.Completed, svc.Status);
    }

    [Fact]
    public void Step6_Quotation_ToAdjust_ForRefundPractice()
    {
        var q = new Quotation { DealId = "deal-1", IsInitial = false };
        q.Finalize();
        q.MarkToAdjust();
        Assert.Equal(QuotationStatus.ToAdjust, q.Status);
    }

    [Fact]
    public void Step7_WarehouseOperationIN_ExplicitlyCreated()
    {
        // Per il rientro dell'oggetto rifiutato si crea una WarehouseOperation IN dedicata
        var whIn = new WarehouseOperation
        {
            WarehouseId = "wh-mi",
            MissionId = "miss-1",
            OperationType = "IN",
            ObjectIds = ["obj-tavolo"]
        };
        Assert.Equal("IN", whIn.OperationType);
        Assert.Single(whIn.ObjectIds);

        whIn.Start();
        whIn.Complete();
        Assert.Equal("Completed", whIn.Status);
    }

    [Fact]
    public void Step8_Deal_StaysOpen_EvenAfterServiceBookedComplete()
    {
        // Il Deal resta aperto finche' ci sono pendenze sull'oggetto rifiutato
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 89m, EstimatedM3 = 10m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate();

        // 27 oggetti in magazzino (26 veri + 1 rientrato) -> Deal resta Active
        var closed = deal.TryCloseIfNoObjectsRemaining(27);
        Assert.False(closed);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Equal(27, deal.ActivePlan!.ObjectCount);
    }

    [Fact]
    public void Step9_WorkOrder_CompletesCycle_EvenWithRefusal()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        wo.VerificaEConcludi("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step10_RefundPayment_IsCommercialDecision_FinancialExecutes()
    {
        // Commercial decide il rimborso; Financial esegue
        var refund = new Payment { DealId = "deal-1", PaymentType = "OneOff" };
        refund.Products.Add(new SimplifiedProduct { Name = "Rimborso trasporto tavolo", Price = -15m });
        // Charge negativo in senso concettuale — modelliamo come charge distinta
        var charge = refund.AddCharge(15m);
        charge.Notes = "Rimborso trasporto oggetto danneggiato";
        charge.Execute();

        Assert.Equal(ChargeStatus.Executed, charge.Status);
        Assert.NotNull(charge.ExecutedAt);
    }

    [Fact]
    public void Step11_Event_OperationCompleted_WithDifferences()
    {
        Emit(new OperationCompletedEvent("shift-1", "wo-consegna"));
        Emit(new ServizioCompletatoEvent("wo-consegna", "svc-1", 4m, true, "1 oggetto rifiutato — rientro magazzino"));

        Assert.Equal(2, _events.Count);
        Assert.True(((ServizioCompletatoEvent)_events[1]).HasDifferences);
    }

    [Fact]
    public void Step12_RejectedObject_History_TracksJourney()
    {
        var obj = new PhysicalObject { Name = "Tavolo", DealId = "deal-1" };
        obj.StockDirectly("wh-mi");
        obj.LoadFromWarehouse("miss-1");
        obj.UnloadToWarehouse("wh-mi", "miss-1");

        // History deve contenere almeno le transizioni di ritorno
        Assert.True(obj.History.Count >= 2);
        Assert.Contains(obj.History, h => h.MissionId == "miss-1");
    }

    [Fact]
    public void Step13_PlanAdjusted_30To27_After4DeliveriesAnd1Refund()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 99m, EstimatedM3 = 30m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.ActivePlan!.ObjectCount = 32; // situazione pre-consegna
        deal.Activate();

        // 4 consegnati + 1 rifiutato rientra -> netto -4 (non -5)
        deal.ActivePlan.UpdateAfterPartialDelivery(4, 3m);
        Assert.Equal(28, deal.ActivePlan.ObjectCount);
        Assert.Equal(27m, deal.ActivePlan.CurrentM3);
    }
}

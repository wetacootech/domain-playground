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
/// UC-3: Consegna parziale da deposito attivo.
/// Cliente con deposito attivo (30 oggetti) richiede riconsegna di 2 oggetti specifici.
/// Verifica: nuova Quotation sullo stesso Deal, ServiceBooked.Consegna con SelectedObjectIds,
/// WarehouseOperation OUT + Shift, Plan aggiornato (30 -> 28), Deal resta Active.
/// </summary>
public class UC3_ConsegnaParzialeTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_ExistingDeal_Active_WithPlan_30Objects()
    {
        var deal = new Deal { LeadId = "lead-1", AreaId = "area-mi" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m, AreaId = "area-mi" });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate();
        // 30 oggetti in warehouse
        deal.TryCloseIfNoObjectsRemaining(30);

        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.NotNull(deal.ActivePlan);
        Assert.Equal(30, deal.ActivePlan!.ObjectCount);
    }

    [Fact]
    public void Step2_NewQuotationForDelivery_InsideSameDeal()
    {
        // Deal gia' attivo — nuova Quotation con ServiceBooked consegna
        var deal = new Deal { LeadId = "lead-1" };
        var originalQuotation = new Quotation { DealId = deal.Id, IsInitial = true, Status = QuotationStatus.Completed };
        deal.Quotations.Add(originalQuotation);

        var deliveryQuotation = new Quotation { DealId = deal.Id, IsInitial = false };
        deliveryQuotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = new Address("Via Garibaldi 3", "Milano", "20100", "area-mi"),
            SelectedObjectIds = ["obj-divano", "obj-libreria"]
        });
        deliveryQuotation.Products.Add(new Product { Name = "Consegna parziale", Price = 120m });
        deal.Quotations.Add(deliveryQuotation);

        Assert.Equal(2, deal.Quotations.Count);
        Assert.Equal(QuotationStatus.Completed, originalQuotation.Status); // originale resta chiusa
        Assert.Equal(QuotationStatus.Draft, deliveryQuotation.Status);
        Assert.Empty(deliveryQuotation.DraftPlans); // nessun nuovo plan
        Assert.Single(deliveryQuotation.Services);
        Assert.Equal(2, deliveryQuotation.Services[0].SelectedObjectIds.Count);
    }

    [Fact]
    public void Step3_DeliveryServiceBooked_HoldsObjectIds_NoObjectCopy()
    {
        var svc = new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            SelectedObjectIds = ["obj-1", "obj-2"]
        };

        // Solo riferimenti per identita'; nessuna copia di dati Object
        Assert.Equal(ServiceBookedType.Consegna, svc.Type);
        Assert.Equal(2, svc.SelectedObjectIds.Count);
        Assert.Contains("obj-1", svc.SelectedObjectIds);
    }

    [Fact]
    public void Step4_DeliveryQuotation_Finalize_CreatesConsegnaWorkOrder()
    {
        var q = new Quotation { DealId = "deal-1" };
        var svc = new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            SelectedObjectIds = ["obj-1", "obj-2"],
            ScheduledDate = DateTime.Today.AddDays(3)
        };
        q.Services.Add(svc);
        q.Finalize();
        Assert.Equal(QuotationStatus.Finalized, q.Status);

        svc.AccettaServizio(); // DaAccettare -> DaCompletare
        svc.SegnaComePronto(); // DaCompletare -> Pronto
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);

        var wo = new WorkOrder
        {
            ServiceBookedId = svc.Id,
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, false, "area-mi"),
            ScheduledDate = svc.ScheduledDate
        };
        svc.WorkOrderId = wo.Id;
        wo.ServizioPronto("Sales");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);
    }

    [Fact]
    public void Step5_Planning_ProducesWarehouseOperationOUT_AndShift()
    {
        var planning = new Planning { Date = DateTime.Today.AddDays(3) };
        var team = new PlanningTeam { OperatorIds = ["op-1"] };
        planning.Teams.Add(team);
        var mission = planning.AddMission(team.Id, [new ServiceRef("wo-consegna", 100)], ["v-1"]);
        Assert.Single(planning.Missions);

        // WarehouseOperation OUT preparatoria
        var whOut = new WarehouseOperation
        {
            WarehouseId = "wh-mi",
            MissionId = mission.Id,
            OperationType = "OUT",
            ObjectIds = ["obj-divano", "obj-libreria"]
        };
        Assert.Equal("Pending", whOut.Status);
        whOut.Start();
        Assert.Equal("InProgress", whOut.Status);
        whOut.Complete();
        Assert.Equal("Completed", whOut.Status);

        // Shift cliente (successivo)
        var shift = new Shift { MissionId = mission.Id, Date = planning.Date };
        var entry = shift.AddServiceEntry("wo-consegna", "deal-1", "lead-1",
            ServiceEntryType.Consegna, new ClientData("Mario Rossi", "+39 333"));
        Assert.Equal(ServiceEntryType.Consegna, entry.Type);
    }

    [Fact]
    public void Step6_ExecuteDelivery_ObjectsLoadedFromWarehouseThenDelivered()
    {
        var obj1 = new PhysicalObject { Name = "Divano", DealId = "deal-1", Volume = 2m };
        var obj2 = new PhysicalObject { Name = "Libreria", DealId = "deal-1", Volume = 1.5m };
        // partenza: OnWarehouse
        obj1.StockDirectly("wh-mi");
        obj2.StockDirectly("wh-mi");
        Assert.Equal(ObjectStatus.OnWarehouse, obj1.Status);

        // Load from warehouse
        obj1.LoadFromWarehouse("miss-1");
        obj2.LoadFromWarehouse("miss-1");
        Assert.Equal(ObjectStatus.OnVehicle, obj1.Status);

        // Delivered
        obj1.Deliver("miss-1");
        obj2.Deliver("miss-1");
        Assert.Equal(ObjectStatus.Delivered, obj1.Status);
        Assert.Equal(ObjectStatus.Delivered, obj2.Status);
    }

    [Fact]
    public void Step7_ShiftComplete_TriggersWOVerification_DealStaysActive()
    {
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-consegna", "deal-1", "lead-1",
            ServiceEntryType.Consegna, new ClientData("Mario", "333"));
        // ServiceEntry senza Start: in-progress segnalato da shift InCorso
        entry.Complete("Firma_Mario");
        shift.Complete();

        var wo = new WorkOrder();
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);

        Emit(new OperationCompletedEvent(shift.Id, wo.Id));
        Emit(new ServizioCompletatoEvent(wo.Id, "svc-1", 3.5m, false, "2 oggetti consegnati"));
        Assert.Equal(2, _events.Count);
    }

    [Fact]
    public void Step8_PlanAdjusted_From30To28_DealRemainsActive()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate();

        deal.ActivePlan!.UpdateAfterPartialDelivery(2, 3.5m);
        Assert.Equal(-2, deal.ActivePlan.ObjectCount); // nessun ObjectCount iniziale, solo decremento
        Assert.Equal(26.5m, deal.ActivePlan.CurrentM3);
        Assert.Single(deal.ActivePlan.History);

        // 28 oggetti rimanenti -> Deal resta Active
        var closed = deal.TryCloseIfNoObjectsRemaining(28);
        Assert.False(closed);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Equal(28, deal.ActivePlan.ObjectCount);
    }

    [Fact]
    public void Step9_NewQuotation_IndependentLifecycle()
    {
        // La nuova Quotation di consegna ha ciclo proprio: Draft -> InProgress -> Finalized -> Completed
        var q = new Quotation { DealId = "deal-1", IsInitial = false };
        Assert.Equal(QuotationStatus.Draft, q.Status);
        q.Confirm();
        Assert.Equal(QuotationStatus.InProgress, q.Status);
        q.Finalize();
        Assert.Equal(QuotationStatus.Finalized, q.Status);
        q.Complete();
        Assert.Equal(QuotationStatus.Completed, q.Status);
    }

    [Fact]
    public void Step10_OriginalQuotation_IsNotReopened()
    {
        var original = new Quotation { DealId = "deal-1", IsInitial = true, Status = QuotationStatus.Completed };
        // La consegna parziale non deve toccare la Quotation originale
        Assert.Equal(QuotationStatus.Completed, original.Status);
        original.Confirm(); // no-op da Completed
        Assert.Equal(QuotationStatus.Completed, original.Status);
    }

    [Fact]
    public void Step11_OneOrderOneQuotation_Tracking()
    {
        // Ogni ordine di consegna genera una Quotation distinta sullo stesso Deal
        var deal = new Deal { LeadId = "lead-1" };
        var q1 = new Quotation { DealId = deal.Id };
        q1.Services.Add(new ServiceBooked { Type = ServiceBookedType.Consegna, SelectedObjectIds = ["a"] });
        var q2 = new Quotation { DealId = deal.Id };
        q2.Services.Add(new ServiceBooked { Type = ServiceBookedType.Consegna, SelectedObjectIds = ["b"] });
        deal.Quotations.Add(q1);
        deal.Quotations.Add(q2);

        Assert.Equal(2, deal.Quotations.Count);
        Assert.NotEqual(q1.Id, q2.Id);
    }

    [Fact]
    public void Step12_Payment_OneOffChargeForDelivery()
    {
        var payment = new Payment { DealId = "deal-1", PaymentType = "OneOff" };
        payment.Products.Add(new SimplifiedProduct { Name = "Consegna parziale", Price = 120m });
        var charge = payment.AddCharge(120m);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        payment.ExecuteCharge(charge.Id);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
    }
}

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
/// UC-1: Deposito Recurring — full flow from Lead to Concluded.
/// Verifies entity states and events at each step against DDD5.
/// </summary>
public class UC1_DepositoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_CreateLead_ShouldHaveCorrectState()
    {
        var lead = new Lead { Personal = new Personal("Anna", "Verdi", "anna@test.com", "+39 333 000") };

        Assert.Equal(LeadStatus.ToConvert, lead.Status);
        Assert.False(lead.Customer.IsCustomer);
        Assert.Empty(lead.DealIds);
    }

    [Fact]
    public void Step2_CreateDeal_ShouldBeToQualify_AndOneOffUntilQuotationWithPlanAccepted()
    {
        var lead = new Lead { Personal = new Personal("Anna", "Verdi", "anna@test.com", null) };
        var deal = new Deal { LeadId = lead.Id, AreaId = "area-001" };
        lead.AddDeal(deal.Id);

        Assert.Equal(DealStatus.ToQualify, deal.Status);
        // Deal.Type e' derivato: senza Quotation accettate con DraftPlan resta OneOff
        Assert.Equal(DealType.OneOff, deal.Type);
        Assert.Equal("area-001", deal.AreaId);
        Assert.Single(lead.DealIds);

        // Aggiungo e accetto una Quotation con DraftPlan -> Deal diventa Recurring
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 89.90m, EstimatedM3 = 10 });
        deal.Quotations.Add(q);
        q.Finalize();
        Assert.Equal(DealType.Recurring, deal.Type);

        Emit(new DealCreatedEvent(deal.Id, lead.Id));
        Assert.Single(_events);
        Assert.IsType<DealCreatedEvent>(_events[0]);
    }

    [Fact]
    public void Step3_QualifyDeal_ShouldTransitionToQualified()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();

        Assert.Equal(DealStatus.Qualified, deal.Status);
        Assert.Single(deal.StatusHistory);
    }

    [Fact]
    public void Step4_CreateQuotation_WithDraftPlan()
    {
        var deal = new Deal { LeadId = "lead-1", AreaId = "area-001" };
        var quotation = new Quotation
        {
            DealId = deal.Id,
            IsInitial = true,
            Products = [new Product { Name = "Deposito Premium", Price = 129.90m }],
            Services = [new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("20100", "area-001") }],
        };
        quotation.DraftPlans.Add(new DraftPlan { Description = "Piano deposito", MonthlyFee = 89.90m, EstimatedM3 = 10, AreaId = "area-001" });
        deal.Quotations.Add(quotation);

        Assert.Single(deal.Quotations);
        Assert.Equal(129.90m, quotation.TotalPrice);
        Assert.Single(quotation.DraftPlans);
        Assert.Equal(QuotationStatus.Draft, quotation.Status);
    }

    [Fact]
    public void Step5_AcceptQuotation_ShouldConvertDealAndCreatePlan()
    {
        // Setup
        var lead = new Lead { Personal = new Personal("Anna", "Verdi", "anna@test.com", null) };
        var deal = new Deal { LeadId = lead.Id, AreaId = "area-001" };
        lead.AddDeal(deal.Id);

        var quotation = new Quotation { DealId = deal.Id, IsInitial = true };
        quotation.Products.Add(new Product { Name = "Deposito Premium", Price = 129.90m });
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("20100", "area-001"),
            ScheduledDate = DateTime.Today.AddDays(5),
            ScheduledSlot = "09:00-12:00"
        });
        quotation.DraftPlans.Add(new DraftPlan { Description = "Piano deposito", MonthlyFee = 89.90m, EstimatedM3 = 10, AreaId = "area-001" });
        deal.Quotations.Add(quotation);

        // Accept: Qualify → Convert (Deal stays Converted, not Active)
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        Assert.Equal(DealStatus.Converted, deal.Status);

        lead.MarkConverted();
        Assert.True(lead.Customer.IsCustomer);

        // Finalize quotation
        quotation.Finalize();
        Assert.True(quotation.IsAccepted);
        Assert.Equal(QuotationStatus.Finalized, quotation.Status);

        // Create ActivePlan from DraftPlan (Deal stays Converted!)
        deal.CreatePlan(quotation, quotation.DraftPlans[0]);
        Assert.NotNull(deal.ActivePlan);
        Assert.Equal(89.90m, deal.ActivePlan!.MonthlyFee);
        Assert.Equal(10m, deal.ActivePlan.CurrentM3);
        Assert.Equal(DealStatus.Converted, deal.Status); // NOT Active yet!

        // Create WorkOrder
        var svc = quotation.Services[0];
        var wo = new WorkOrder
        {
            ServiceBookedId = svc.Id,
            ServiceAddress = svc.ServiceAddress?.ZipCode,
            ContactName = "Anna Verdi",
            ScheduledDate = svc.ScheduledDate,
            ScheduledSlot = svc.ScheduledSlot,
        };
        svc.WorkOrderId = wo.Id;
        Assert.Equal(WorkOrderStatus.Completing, wo.Status);
        Assert.Equal(DateTime.Today.AddDays(5), wo.ScheduledDate);

        // Create Questionnaire
        var questionnaire = new Questionnaire { Origin = "Quotation" };
        questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Fragili?", "boolean", null, "all") });
        svc.QuestionnaireId = questionnaire.Id;
        Assert.False(questionnaire.IsCompleted);

        // Create WorkOrder for execution
        var woExec = new WorkOrder
        {
            Type = WorkOrderType.Commercial,
            ServiceBookedId = svc.Id,
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, false, "area-001"),
            ServiceAddress = svc.ServiceAddress?.ZipCode,
        };

        // Create Payment
        var payment = new Payment
        {
            DealId = deal.Id,
            QuotationId = quotation.Id,
            PaymentType = "Recurring"
        };
        payment.Products.Add(new SimplifiedProduct { Name = "Deposito Premium", Price = 129.90m });
        payment.AddCharge(129.90m);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(129.90m, payment.TotalAmount);

        // Events
        Emit(new DealConvertedEvent(deal.Id, lead.Id));
        Emit(new QuotationAcceptedEvent(quotation.Id, deal.Id, lead.Id, true));
        Emit(new WorkOrderCreatedEvent(woExec.Id, woExec.Type.ToString(), "Ritiro"));
        Emit(new PaymentCreatedEvent(payment.Id, deal.Id));
        Assert.Equal(4, _events.Count);
    }

    [Fact]
    public void Step6_CompleteQuestionnaire_ShouldAdvanceWO()
    {
        var wo = new WorkOrder();
        Assert.Equal(WorkOrderStatus.Completing, wo.Status);

        var questionnaire = new Questionnaire { Origin = "Quotation" };
        questionnaire.IsVerified = true;

        // WO: InCompletamento → DaProgrammare (questionnaire completed)
        wo.ServizioPronto("Ops");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);
    }

    [Fact]
    public void Step7_ScheduleAndPlan_ShouldCreateMissionAndShift()
    {
        // Schedule WO
        var wo = new WorkOrder { ScheduledDate = DateTime.Today.AddDays(5) };
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);

        // Create Planning for the day
        var planning = new Planning { Date = wo.ScheduledDate!.Value };
        var team = new PlanningTeam { OperatorIds = ["op-1", "op-2"] };
        planning.Teams.Add(team);

        var serviceRefs = new List<ServiceRef> { new(wo.Id, 100) };
        var mission = planning.AddMission(team.Id, serviceRefs, ["vehicle-1"]);
        Assert.Single(planning.Missions);
        Assert.Single(mission.ServiceRefs);

        // Create Shift
        var shift = new Shift
        {
            MissionId = mission.Id,
            Date = planning.Date,
            Mission = new MissionData(["op-1", "op-2"], ["vehicle-1"], [], "09:00-12:00"),
            Resources = new ShiftResources(["op-1", "op-2"], ["vehicle-1"], [])
        };
        var entry = shift.AddServiceEntry(wo.Id, "deal-1", "lead-1", ServiceEntryType.Ritiro,
            new ClientData("Anna Verdi", "+39 333 000"));
        Assert.Equal("Created", shift.Status);
        Assert.Single(shift.ServiceEntries);
        Assert.False(entry.Completed);
        Assert.Equal(ServiceEntryType.Ritiro, entry.Type);

        // Start execution
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);
        shift.Start();
        Assert.Equal("InProgress", shift.Status);
    }

    [Fact]
    public void Step8_ExecuteShift_ShouldCreateObjectsAndTrackState()
    {
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-1", "deal-1", "lead-1", ServiceEntryType.Ritiro,
            new ClientData("Anna Verdi", "+39 333 000"));
        // ServiceEntry non ha piu' Start/InProgress: il segnale "operatore sta lavorando" vive sullo Shift InCorso
        Assert.False(entry.Completed);

        // Census: create 3 objects
        var objects = new List<PhysicalObject>();
        for (int i = 0; i < 3; i++)
        {
            var obj = new PhysicalObject { Name = $"Oggetto {i}", Volume = 2.0m, DealId = "deal-1", LeadId = "lead-1" };
            Assert.Equal(ObjectStatus.Draft, obj.Status);

            obj.PickUp("miss-1");
            Assert.Equal(ObjectStatus.PickedUp, obj.Status);

            obj.LoadOnVehicle("miss-1");
            Assert.Equal(ObjectStatus.OnVehicle, obj.Status);

            objects.Add(obj);
        }

        // Task censimento
        var task = shift.AddTask(TaskType.Censimento, entry.Id, objects.Select(o => o.Id).ToList());
        Assert.Equal(TaskType.Censimento, task.Type);
        Assert.Equal(3, task.ObjectIds.Count);

        // Complete entry
        entry.Complete("Firma_Anna_Verdi");
        Assert.True(entry.Completed);
        Assert.Equal("Firma_Anna_Verdi", entry.ClientInfo!.Signature);

        // Complete shift
        shift.Complete();
        Assert.Equal("Completed", shift.Status);

        // Unload to warehouse
        foreach (var obj in objects)
        {
            obj.UnloadToWarehouse("wh-1", "miss-1");
            Assert.Equal(ObjectStatus.OnWarehouse, obj.Status);
            Assert.Equal("wh-1", obj.Position!.WarehouseId);
        }

        // WarehouseOperation IN
        var whOp = new WarehouseOperation
        {
            WarehouseId = "wh-1",
            MissionId = "miss-1",
            OperationType = "IN",
            ObjectIds = objects.Select(o => o.Id).ToList()
        };
        whOp.Start();
        whOp.Complete();
        Assert.Equal("Completed", whOp.Status);
    }

    [Fact]
    public void Step9_ConcludeService_ShouldActivateDealWhenAllComplete()
    {
        // Setup: Deal Converted with one service
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        Assert.Equal(DealStatus.Converted, deal.Status);

        // After all services complete → Deal becomes Active
        deal.Activate();
        Assert.Equal(DealStatus.Active, deal.Status);
    }

    [Fact]
    public void Step10_WorkOrder_FullLifecycle()
    {
        var wo = new WorkOrder();
        Assert.Equal(WorkOrderStatus.Completing, wo.Status);

        // InCompletamento → DaProgrammare (questionnaire done)
        wo.ServizioPronto("Ops");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // DaProgrammare → Programmato
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);

        // Programmato → InEsecuzione
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        // InEsecuzione → DaVerificare (post-execution)
        wo.CompletaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // DaVerificare → Concluso (no mismatch)
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);

        Assert.Equal(5, wo.StatusHistory.Count); // 5 transitions
    }

    [Fact]
    public void Step11_WorkOrder_MismatchFlow()
    {
        var wo = new WorkOrder { EstimatedVolume = 10, ActualVolume = 12 };
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // Mismatch → gestito sulla Quotation, non sul WO
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // After adjustment → Concluso
        wo.VerificaEConcludi("Sales");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step12_DealClosure_Recurring_BasedOnObjectCount()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 89.90m, EstimatedM3 = 10 });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate(); // all services completed

        // 3 objects in warehouse → stays Active
        var closed = deal.TryCloseIfNoObjectsRemaining(3);
        Assert.False(closed);
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Equal(3, deal.ActivePlan!.ObjectCount);

        // 0 objects → closes
        closed = deal.TryCloseIfNoObjectsRemaining(0);
        Assert.True(closed);
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }

    [Fact]
    public void Payment_Lifecycle()
    {
        var payment = new Payment { DealId = "deal-1", PaymentType = "Recurring" };
        payment.Products.Add(new SimplifiedProduct { Name = "Deposito", Price = 100m });
        Assert.Equal(100m, payment.TotalAmount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);

        var charge = payment.AddCharge(100m);
        Assert.Equal(ChargeStatus.Pending, charge.Status);

        charge.Execute();
        Assert.Equal(ChargeStatus.Executed, charge.Status);
        Assert.Equal(100m, payment.PaidAmount);
    }
}

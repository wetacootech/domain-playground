using WeTacoo.Domain.Operational.Enums;
using WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.ValueObjects;
using WeTacoo.Domain.Operational;
using WeTacoo.Domain.Operational.Entities;
using WeTacoo.Domain.Operational.ValueObjects;
using WeTacoo.Domain.Execution;
using WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Execution.Enums;
using WeTacoo.Domain.Financial;
using WeTacoo.Domain.Financial.Enums;
using WeTacoo.Domain.Events;

namespace WeTacoo.Tests;

/// <summary>
/// UC-2: Trasloco punto-punto (OneOff) — Ritiro MI + Consegna TO in singola Mission.
/// Verifies: no ActivePlan, 2 ServiceEntry types, 2 firme, Deal Converted → Active → Concluded.
/// </summary>
public class UC2_TraslocoTests
{
    [Fact]
    public void Step1_DealOneOff_NoActivePlan()
    {
        var deal = new Deal { LeadId = "lead-1", AreaId = "area-mi" };
        Assert.Equal(DealType.OneOff, deal.Type);
        Assert.Null(deal.ActivePlan);
    }

    [Fact]
    public void Step2_QuotationTrasloco_TwoServices_NoDraftPlan()
    {
        var quotation = new Quotation { DealId = "deal-1", IsInitial = true };
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("20100", "area-mi"),
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "09:00-12:00"
        });
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = new Address("10121", "area-to"),
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "14:00-17:00"
        });
        quotation.Products.Add(new Product { Name = "Trasloco bilocale", Price = 890m });
        quotation.Products.Add(new Product { Name = "Supplemento MI→TO", Price = 150m });

        Assert.Equal(2, quotation.Services.Count);
        Assert.Empty(quotation.DraftPlans); // No plan for OneOff
        Assert.Equal(1040m, quotation.TotalPrice);

        // Multiregione: services have different areas
        Assert.Equal("area-mi", quotation.Services[0].ServiceAddress!.AreaId);
        Assert.Equal("area-to", quotation.Services[1].ServiceAddress!.AreaId);
    }

    [Fact]
    public void Step3_AcceptQuotation_DealConverted_NoActivePlan()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();

        Assert.Equal(DealStatus.Converted, deal.Status);
        Assert.Null(deal.ActivePlan); // OneOff: no plan

        // No DraftPlans → no CreatePlan call
        // Deal stays Converted (not Active)
    }

    [Fact]
    public void Step4_TwoWorkOrders()
    {
        // Ritiro WO
        var woRitiro = new WorkOrder
        {
            ServiceAddress = "Via Torino 5, Milano",
            ContactName = "Laura Bianchi",
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "09:00-12:00"
        };
        // Consegna WO
        var woConsegna = new WorkOrder
        {
            ServiceAddress = "Corso Francia 10, Torino",
            ContactName = "Laura Bianchi",
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "14:00-17:00"
        };

        Assert.Equal(WorkOrderStatus.Completing, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.Completing, woConsegna.Status);

        // WO with IsTrasloco flag
        var woRitiroTyped = new WorkOrder
        {
            ServiceBookedId = "svc-r",
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, true, "area-mi")
        };
        var woConsegnaTyped = new WorkOrder
        {
            ServiceBookedId = "svc-c",
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, true, "area-to")
        };

        Assert.True(woRitiroTyped.ServiceType.IsTrasloco);
        Assert.True(woConsegnaTyped.ServiceType.IsTrasloco);
    }

    [Fact]
    public void Step5_SingleMission_TwoServiceRefs()
    {
        var planning = new Planning { Date = DateTime.Today.AddDays(7) };
        var team = new PlanningTeam { OperatorIds = ["op-1", "op-2"] };
        planning.Teams.Add(team);

        // Single mission with 2 service refs (ritiro 100% + consegna 100%)
        var serviceRefs = new List<ServiceRef>
        {
            new("wo-ritiro", 100),
            new("wo-consegna", 100)
        };
        var mission = planning.AddMission(team.Id, serviceRefs, ["vehicle-1"]);

        Assert.Single(planning.Missions);
        Assert.Equal(2, mission.ServiceRefs.Count);
    }

    [Fact]
    public void Step6_Shift_TwoServiceEntries_TraslocoTypes()
    {
        var shift = new Shift
        {
            Date = DateTime.Today.AddDays(7),
            MissionId = "miss-1",
            Mission = new MissionData(["op-1", "op-2"], ["vehicle-1"], [], "09:00-17:00"),
            Resources = new ShiftResources(["op-1", "op-2"], ["vehicle-1"], [])
        };

        // Two entries: TraslocoRitiro + TraslocoConsegna
        var entryRitiro = shift.AddServiceEntry("wo-ritiro", "deal-1", "lead-1",
            ServiceEntryType.TraslocoRitiro, new ClientData("Laura Bianchi", "+39 340 000"));
        var entryConsegna = shift.AddServiceEntry("wo-consegna", "deal-1", "lead-1",
            ServiceEntryType.TraslocoConsegna, new ClientData("Laura Bianchi", "+39 340 000"));

        Assert.Equal(2, shift.ServiceEntries.Count);
        Assert.Equal(ServiceEntryType.TraslocoRitiro, entryRitiro.Type);
        Assert.Equal(ServiceEntryType.TraslocoConsegna, entryConsegna.Type);
    }

    [Fact]
    public void Step7_ExecuteTrasloco_ObjectsPickedAndDelivered_NoWarehouse()
    {
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();

        var entryRitiro = shift.AddServiceEntry("wo-r", "deal-1", "lead-1",
            ServiceEntryType.TraslocoRitiro, new ClientData("Laura", "000"));
        var entryConsegna = shift.AddServiceEntry("wo-c", "deal-1", "lead-1",
            ServiceEntryType.TraslocoConsegna, new ClientData("Laura", "000"));

        // Ritiro phase (ServiceEntry non ha piu' Start; l'in-progress vive sullo Shift InCorso)
        var objects = new List<PhysicalObject>();
        for (int i = 0; i < 5; i++)
        {
            var obj = new PhysicalObject { Name = $"Obj {i}", Volume = 0.5m, DealId = "deal-1" };
            obj.PickUp("miss-1");
            obj.LoadOnVehicle("miss-1");
            objects.Add(obj);
        }
        var taskCensimento = shift.AddTask(TaskType.Censimento, entryRitiro.Id, objects.Select(o => o.Id).ToList());
        var taskCarico = shift.AddTask(TaskType.Carico, entryRitiro.Id, objects.Select(o => o.Id).ToList());
        entryRitiro.Complete("Firma_Ritiro_Laura");
        Assert.True(entryRitiro.Completed);
        Assert.Equal("Firma_Ritiro_Laura", entryRitiro.ClientInfo!.Signature);

        // Movimento task (transversal, no serviceEntryId)
        var taskMovimento = shift.AddTask(TaskType.Movimento, null);
        Assert.Null(taskMovimento.ServiceEntryId);

        // Consegna phase (SE senza Start)
        foreach (var obj in objects)
        {
            obj.Deliver("miss-1"); // OnVehicle → Delivered (no warehouse!)
            Assert.Equal(ObjectStatus.Delivered, obj.Status);
        }
        var taskScarico = shift.AddTask(TaskType.Scarico, entryConsegna.Id, objects.Select(o => o.Id).ToList());
        entryConsegna.Complete("Firma_Consegna_Laura");
        Assert.True(entryConsegna.Completed);

        // Two distinct signatures
        Assert.NotEqual(entryRitiro.ClientInfo!.Signature, entryConsegna.ClientInfo!.Signature);

        // Shift complete
        shift.Complete();
        Assert.Equal("Completed", shift.Status);
        Assert.Equal(4, shift.Tasks.Count); // censimento + carico + movimento + scarico

        // All objects Delivered, none OnWarehouse
        Assert.All(objects, o => Assert.Equal(ObjectStatus.Delivered, o.Status));
        Assert.DoesNotContain(objects, o => o.Status == ObjectStatus.OnWarehouse);
    }

    [Fact]
    public void Step8_AllServicesComplete_DealActivatedThenConcluded()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        Assert.Equal(DealStatus.Converted, deal.Status);

        // All services completed → Deal becomes Active
        deal.Activate();
        Assert.Equal(DealStatus.Active, deal.Status);
        Assert.Null(deal.ActivePlan); // OneOff has no plan

        // No objects on warehouse, services done → Conclude
        var closed = deal.TryCloseIfNoObjectsRemaining(0, allServicesCompleted: true);
        Assert.True(closed);
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }

    [Fact]
    public void Step9_Payment_OneOff()
    {
        var payment = new Payment { DealId = "deal-1", PaymentType = "OneOff" };
        payment.Products.Add(new SimplifiedProduct { Name = "Trasloco bilocale", Price = 890m });
        payment.Products.Add(new SimplifiedProduct { Name = "Supplemento", Price = 150m });
        Assert.Equal(1040m, payment.TotalAmount);

        var charge = payment.AddCharge(1040m);
        payment.ExecuteCharge(charge.Id);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
    }

    [Fact]
    public void WorkOrder_FullLifecycle_ForTrasloco()
    {
        var wo = new WorkOrder { ScheduledDate = DateTime.Today.AddDays(7) };

        // Questionnaire completed → DaProgrammare
        wo.ServizioPronto("Ops");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // Programmato
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);

        // InEsecuzione
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        // Post-execution → DaVerificare
        wo.CompletaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // Concluso
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void DealStatus_InvalidTransitions()
    {
        var deal = new Deal { };

        // Can't activate from ToQualify
        deal.Activate();
        Assert.Equal(DealStatus.ToQualify, deal.Status);

        // Can't conclude from ToQualify
        deal.Conclude();
        Assert.Equal(DealStatus.ToQualify, deal.Status);

        // Can't convert from ToQualify (must qualify first)
        deal.Convert();
        Assert.Equal(DealStatus.ToQualify, deal.Status);

        // Qualify then convert works
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();
        Assert.Equal(DealStatus.Converted, deal.Status);

        // Can't discard after conversion
        deal.Discard();
        Assert.Equal(DealStatus.Converted, deal.Status);
    }
}

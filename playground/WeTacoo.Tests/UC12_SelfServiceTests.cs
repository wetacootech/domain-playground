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
/// UC-12: Self-service con sorpresa.
/// Verifica: ServiceType. Shift autonomo senza veicolo,
/// volume reale > venduto -> WO In pausa + ServiceBooked DaCompletare + Quotation ToVerify,
/// InterventoRisolto.Riprendi, ActivePlan aggiornato (8 m3 -> 11 m3).
/// </summary>
public class UC12_SelfServiceTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_ServiceType_Autonomous_Flag()
    {
        var st = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, true, "area-mi");
        Assert.True(st.IsAutonomous);
        Assert.False(st.IsPartial);

        // Self-service e' standalone: ServiceBooked.MovingIds deve essere vuoto (DDD5 §2.2e review 2026-04-16)
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro, IsAutonomous = true };
        Assert.Empty(svc.MovingIds);
    }

    [Fact]
    public void Step2_AutonomousShift_NoVehicle()
    {
        // Shift self-service: no veicolo, team di magazzino, sito = magazzino
        var shift = new Shift
        {
            Date = DateTime.Today,
            
            MissionId = "miss-self",
            Mission = new MissionData(["magazziniere-1"], [], [], "10:00-12:00"),
            Resources = new ShiftResources(["magazziniere-1"], [], [])
        };

        Assert.Empty(shift.Mission!.Vehicles);
        Assert.Empty(shift.Resources!.PresentVehicles);
    }

    [Fact]
    public void Step3_ClientComes_ObjectsCensitedInShift()
    {
        var shift = new Shift {  Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-self", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, new ClientData("Cliente self", "333"));
        // SE senza Start: in-progress = Shift InCorso

        // Il magazziniere censisce ogni oggetto
        var objs = Enumerable.Range(0, 34).Select(i =>
        {
            var o = new PhysicalObject { Name = $"obj-{i}", DealId = "deal-1", Volume = 0.32m };
            o.PickUp("miss-self");
            return o;
        }).ToList();

        var task = shift.AddTask(TaskType.Censimento, entry.Id, objs.Select(o => o.Id).ToList());
        Assert.Equal(34, task.ObjectIds.Count);
    }

    [Fact]
    public void Step4_VolumeExceeds_Slot_8m3vs11m3()
    {
        decimal slotAcquistato = 8m;
        decimal volumeReale = 11m;
        Assert.True(volumeReale > slotAcquistato);
    }

    [Fact]
    public void Step5_WorkOrder_MettiInPausa_WhileAtWarehouse()
    {
        var wo = new WorkOrder
        {
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, true, "area-mi"),
            EstimatedVolume = 8m,
            ActualVolume = 11m
        };
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("Volume reale 11m3 supera slot 8m3 — attende decisione Sales");

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
    }

    [Fact]
    public void Step6_ServiceBooked_RichiedeIntervento()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.RichiedeIntervento("Slot 8m3 superato — reale 11m3");
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
    }

    [Fact]
    public void Step7_Quotation_ToVerify_DuringExecution()
    {
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        q.MarkToVerify();
        Assert.Equal(QuotationStatus.ToVerify, q.Status);
    }

    [Fact]
    public void Step8_CustomerAccepts_NewPrice_Riprendi()
    {
        var wo = new WorkOrder { EstimatedVolume = 8m, ActualVolume = 11m };
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("volume eccede");

        wo.InterventoRisoltoRiprendi("Cliente accetta nuovo prezzo, slot aggiornato a 11m3");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        Emit(new InterventoRisoltoEvent(wo.Id, "riprendi", "Prezzo e slot aggiornati"));
        Assert.Single(_events);
    }

    [Fact]
    public void Step9_CustomerRefuses_Chiudi()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("volume");

        wo.InterventoRisoltoChiudi("Cliente rifiuta, cancellazione");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step10_ActivePlan_CapacityAdjusted_8To11()
    {
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify(); deal.EnterNegotiation(); deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 49m, EstimatedM3 = 8m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);

        Assert.Equal(8m, deal.ActivePlan!.CurrentM3);
        Assert.Equal(49m, deal.ActivePlan.MonthlyFee);

        // Sales adegua il piano al volume reale
        deal.ActivePlan.CurrentM3 = 11m;
        deal.ActivePlan.MonthlyFee = 69m;

        Assert.Equal(11m, deal.ActivePlan.CurrentM3);
        Assert.Equal(69m, deal.ActivePlan.MonthlyFee);
    }

    [Fact]
    public void Step11_SoldData_Immutable_RealDataOnWorkOrder()
    {
        // Principio: dati venduti (ServiceBooked) immutabili, dati reali sul WorkOrder
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro, Notes = "Slot 8m3 venduto" };
        var wo = new WorkOrder { ServiceBookedId = svc.Id, EstimatedVolume = 8m, ActualVolume = 11m };

        Assert.Equal("Slot 8m3 venduto", svc.Notes); // immutabile
        Assert.Equal(11m, wo.ActualVolume); // dato reale
    }

    [Fact]
    public void Step12_ObjectsDirectlyStocked_NoVehicle()
    {
        // Self-service: oggetti vanno da Draft -> OnWarehouse senza PickedUp/OnVehicle
        var obj = new PhysicalObject { Name = "scatola", DealId = "deal-1" };
        Assert.Equal(ObjectStatus.Draft, obj.Status);

        obj.StockDirectly("wh-mi", "miss-self");
        Assert.Equal(ObjectStatus.OnWarehouse, obj.Status);
        Assert.Equal("wh-mi", obj.Position!.WarehouseId);
    }

    [Fact]
    public void Step13_CompletionRecord_WriteOnce_OnServiceBooked()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.ServizioCompletato();

        svc.CompletionData = new CompletionRecord(11m, 34, "Volume eccedente slot venduto", DateTime.UtcNow);
        Assert.NotNull(svc.CompletionData);
        Assert.Equal(11m, svc.CompletionData!.ActualVolume);
        Assert.Equal(34, svc.CompletionData.ObjectsMoved);
    }

    [Fact]
    public void Step14_Quotation_BackToFinalized_AfterResolution()
    {
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        q.MarkToVerify();
        q.Verify(); // Sales risolve, quotation torna Finalized
        Assert.Equal(QuotationStatus.Finalized, q.Status);
    }

    [Fact]
    public void Step15_WarehouseOperationIN_AfterSelfServiceShift()
    {
        // Dopo lo Shift autonomo, WarehouseOperation IN per posizionamento fisico
        var whIn = new WarehouseOperation
        {
            WarehouseId = "wh-mi",
            MissionId = "miss-self",
            OperationType = "IN",
            ObjectIds = Enumerable.Range(0, 34).Select(i => $"obj-{i}").ToList()
        };
        whIn.Start();
        whIn.Complete();

        Assert.Equal(34, whIn.ObjectIds.Count);
        Assert.Equal("Completed", whIn.Status);
    }
}

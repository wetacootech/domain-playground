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
/// UC-6: Problema in esecuzione che rimbalza al commerciale.
/// Pattern InterventoRisolto: WO InEsecuzione -> MettiInPausa (In pausa) -> InterventoRisolto{Riprogramma|Riprendi|Chiudi}.
/// ServiceBooked Pronto -> DaCompletare (richiedeIntervento) -> InterventoRisolto -> Pronto.
/// Quotation Finalizzato -> ToVerify -> Finalizzato.
/// </summary>
public class UC6_ProblemaEsecuzioneTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_ShiftRegistersProblem_OnTheField()
    {
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        shift.Problems.Add("Seminterrato allagato — serve pompa e giorno extra");

        Assert.Single(shift.Problems);
        Assert.Equal("InProgress", shift.Status);
    }

    [Fact]
    public void Step2_ShiftPartialCompletion_WithProblems()
    {
        // DDD5 review 2026-04-14: no partial stato sulla SE; lo Shift chiude Sospesa
        // con ServiceEntryOutcome nel payload per le SE non completate.
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-1", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, new ClientData("Cliente", "333"));
        // entry resta Completed = false: ritirati solo piani superiori
        shift.Problems.Add("Solo piani superiori ritirati — seminterrato allagato");
        shift.Suspend(); // Sospeso: outcome parziale nel payload

        var outcome = new ServiceEntryOutcome(entry.Id, "parziale", "seminterrato allagato");
        Assert.Equal("parziale", outcome.Outcome);
        Assert.False(entry.Completed);
        Assert.Equal("Suspended", shift.Status);
        Assert.Single(shift.Problems);
    }

    [Fact]
    public void Step3_WorkOrder_InEsecuzione_MettiInPausa()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        wo.MettiInPausa("Seminterrato allagato — serve decisione commerciale");
        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.Contains(wo.StatusHistory, h => h.Contains("Seminterrato"));
    }

    [Fact]
    public void Step4_Operational_EmitsRichiedeInterventoToCommercial()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("attrezzatura mancante");

        Emit(new RichiedeInterventoEvent(wo.Id, "svc-1", "Seminterrato allagato, serve pompa"));
        Assert.Single(_events);
        Assert.Equal("Operational", _events[0].SourceBC);
        Assert.Equal("Commercial", _events[0].TargetBC);
    }

    [Fact]
    public void Step5_ServiceBooked_RichiedeIntervento_FromPronto()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);

        svc.RichiedeIntervento("Seminterrato allagato — rinegoziare extra");
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
        Assert.Contains(svc.StatusHistory, h => h.Contains("Seminterrato"));
    }

    [Fact]
    public void Step6_Quotation_Finalizzato_MarkToVerify()
    {
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        Assert.Equal(QuotationStatus.Finalized, q.Status);

        q.MarkToVerify();
        Assert.Equal(QuotationStatus.ToVerify, q.Status);
    }

    [Fact]
    public void Step7_CustomerAcceptsExtra_ChainResolves_Riprogramma()
    {
        // Cliente accetta -> Sales risolve -> InterventoRisolto con azione "riprogramma"
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.RichiedeIntervento("allagato");

        svc.InterventoRisolto();
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);

        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        q.MarkToVerify();
        q.Verify();
        Assert.Equal(QuotationStatus.Finalized, q.Status);

        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("allagato");
        wo.InterventoRisoltoRiprogramma("nuovo giorno+pompa");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        Emit(new InterventoRisoltoEvent(wo.Id, "riprogramma", "nuovo giorno+pompa"));
        Assert.Single(_events);
    }

    [Fact]
    public void Step8_CustomerRefuses_ChainResolves_Chiudi()
    {
        // Cliente rifiuta -> WO chiude a 75 oggetti
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("cliente non vuole extra");

        wo.InterventoRisoltoChiudi("chiuso a 75 oggetti, residui in nuova Quotation");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step9_InterventoRisoltoRiprendi_FromPausa_ToInEsecuzione()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("decisione commerciale pendente");

        wo.InterventoRisoltoRiprendi("cliente ha accettato al volo");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);
    }

    [Fact]
    public void Step10_WorkOrder_InPausa_CannotBeProgrammedDirectly()
    {
        // Da InPausa bisogna passare attraverso InterventoRisoltoRiprogramma/Riprendi/Chiudi
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("...");

        // Programma non e' applicabile da InPausa
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Paused, wo.Status);

        // AvviaEsecuzione non applicabile
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
    }

    [Fact]
    public void Step11_SecondShift_CreatedAfterRiprogramma()
    {
        // Nuovo Shift per il giorno extra post-riprogramma, legato a nuova Mission
        var shift2 = new Shift
        {
            MissionId = "miss-2",
            Date = DateTime.Today.AddDays(2),
            Mission = new MissionData(["op-1", "op-2"], ["v-1"], ["pompa"], "09:00-13:00")
        };
        shift2.Start();
        Assert.Equal("InProgress", shift2.Status);
        Assert.Contains("pompa", shift2.Mission!.Assets);
    }

    [Fact]
    public void Step12_NewObjects_CreatedInSecondShift_SameService()
    {
        // Gli oggetti del seminterrato non esistono ancora — sono creati nel secondo Shift
        var newObjs = Enumerable.Range(0, 20).Select(i => new PhysicalObject
        {
            Name = $"seminterrato-{i}",
            DealId = "deal-1",
            Volume = 1m
        }).ToList();

        foreach (var o in newObjs)
        {
            o.PickUp("miss-2");
            o.LoadOnVehicle("miss-2");
        }
        Assert.All(newObjs, o => Assert.Equal(ObjectStatus.OnVehicle, o.Status));
        Assert.All(newObjs, o => Assert.Equal("deal-1", o.DealId));
    }

    [Fact]
    public void Step13_StatesAcrossLevels_AreConsistent()
    {
        // Verifica che tutti i livelli siano nello stato atteso
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.MettiInPausa("problema");

        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.RichiedeIntervento("problema");

        var q = new Quotation { DealId = "d-1" };
        q.Finalize();
        q.MarkToVerify();

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
        Assert.Equal(QuotationStatus.ToVerify, q.Status);
    }

    [Fact]
    public void Step14_OperatorAndCommercial_NeverCommunicateDirectly()
    {
        // Principio: Operational filtra la comunicazione
        // Verifichiamo che gli eventi dal campo siano indirizzati a Operational, non a Commercial
        var shift = new Shift { Date = DateTime.Today };
        shift.Problems.Add("problema");
        Emit(new OperationCompletedEvent(shift.Id, "wo-1"));

        // La comunicazione a Commercial avviene solo dopo qualifica Operational
        Emit(new RichiedeInterventoEvent("wo-1", "svc-1", "problema qualificato"));

        Assert.Equal("Execution", _events[0].SourceBC);
        Assert.Equal("Operational", _events[0].TargetBC);
        Assert.Equal("Operational", _events[1].SourceBC);
        Assert.Equal("Commercial", _events[1].TargetBC);
    }
}

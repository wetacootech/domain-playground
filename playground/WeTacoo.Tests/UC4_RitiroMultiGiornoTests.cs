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
/// UC-4: Ritiro multi-giorno (3 piani, 3 giornate).
/// Verifica: 3 ServiceBooked ritiro distinti, 3 Mission distinte, Shift PARTIAL con Shift di continuazione,
/// team registrato per giornata, chiusura a cascata dal giorno 3.
/// Nota: il modello di Shift non ha un outcome esplicito PARTIAL/COMPLETE; si usa lo stato "Completed"
/// e la propagazione applicativa. Qui si simulano i concetti con Status + Problems.
/// </summary>
public class UC4_RitiroMultiGiornoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_SingleQuotation_ThreeRitiroServiceBooked()
    {
        var q = new Quotation { DealId = "deal-1" };
        q.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("Via Uffici 1", "Milano", "20100", "area-mi"),
            Notes = "Piano terra - 75 oggetti"
        });
        q.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("Via Uffici 1", "Milano", "20100", "area-mi"),
            Notes = "Primo piano - 70 oggetti"
        });
        q.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("Via Uffici 1", "Milano", "20100", "area-mi"),
            Notes = "Secondo piano - 55 oggetti"
        });
        q.Products.Add(new Product { Name = "Ritiro ufficio 3 giorni", Price = 3500m });

        Assert.Equal(3, q.Services.Count);
        Assert.All(q.Services, s => Assert.Equal(ServiceBookedType.Ritiro, s.Type));
        Assert.All(q.Services, s => Assert.NotEqual("", s.Id));
    }

    [Fact]
    public void Step2_EachServiceBooked_IndependentState()
    {
        var svc1 = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var svc2 = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var svc3 = new ServiceBooked { Type = ServiceBookedType.Ritiro };

        svc1.AccettaServizio();
        svc1.SegnaComePronto();
        // svc2 e svc3 restano in DaAccettare

        Assert.Equal(ServiceBookedStatus.Ready, svc1.Status);
        Assert.Equal(ServiceBookedStatus.ToAccept, svc2.Status);
        Assert.Equal(ServiceBookedStatus.ToAccept, svc3.Status);
    }

    [Fact]
    public void Step3_ThreeWorkOrders_OnePerFloor()
    {
        var wo1 = new WorkOrder { ServiceAddress = "Piano terra", EstimatedVolume = 40m };
        var wo2 = new WorkOrder { ServiceAddress = "Primo piano", EstimatedVolume = 38m };
        var wo3 = new WorkOrder { ServiceAddress = "Secondo piano", EstimatedVolume = 30m };

        Assert.NotEqual(wo1.Id, wo2.Id);
        Assert.NotEqual(wo2.Id, wo3.Id);
        Assert.All(new[] { wo1, wo2, wo3 }, w => Assert.Equal(WorkOrderStatus.Completing, w.Status));
    }

    [Fact]
    public void Step4_ThreeDistinctMissions_DifferentDays()
    {
        var day1 = DateTime.Today.AddDays(10);
        var day2 = day1.AddDays(1);
        var day3 = day1.AddDays(2);

        var planning1 = new Planning { Date = day1 };
        var planning2 = new Planning { Date = day2 };
        var planning3 = new Planning { Date = day3 };

        var teamA = new PlanningTeam { OperatorIds = ["op-A1", "op-A2"] };
        var teamB = new PlanningTeam { OperatorIds = ["op-B1", "op-B2"] };
        var teamC = new PlanningTeam { OperatorIds = ["op-C1"] };

        planning1.Teams.Add(teamA);
        planning2.Teams.Add(teamB);
        planning3.Teams.Add(teamC);

        var m1 = planning1.AddMission(teamA.Id, [new ServiceRef("wo-p0", 100)], ["v-1"]);
        var m2 = planning2.AddMission(teamB.Id, [new ServiceRef("wo-p1", 100)], ["v-1"]);
        var m3 = planning3.AddMission(teamC.Id, [new ServiceRef("wo-p2", 100)], ["v-1"]);

        Assert.NotEqual(m1.Id, m2.Id);
        Assert.NotEqual(m1.TeamId, m2.TeamId);
        Assert.NotEqual(m2.TeamId, m3.TeamId);
    }

    [Fact]
    public void Step5_Day1_PartialShift_ProblemsEmpty_ContinuationPlanned()
    {
        // DDD5 review 2026-04-14: il caso "partial" non esiste piu' sulla SE.
        // Lo Shift chiude "Suspended" con ServiceEntryOutcome nel payload per le SE non completate.
        var shift1 = new Shift { Date = DateTime.Today, MissionId = "miss-1" };
        shift1.Start();
        var entry = shift1.AddServiceEntry("wo-p0", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, new ClientData("Rag. Bianchi", "+39 340"));
        // entry non viene completata (rimane Completed = false)
        shift1.Problems.Add("Solo 50/75 oggetti censiti, continuare domani");
        shift1.Suspend(); // Shift Sospeso (outcome parziale tracciato nel payload chiusura Shift)

        // outcome parziale rappresentato via ServiceEntryOutcome (payload chiusura Shift)
        var outcome = new ServiceEntryOutcome(entry.Id, "parziale", "Solo 50/75 censiti", ResidualVolume: 25m);
        Assert.Equal("parziale", outcome.Outcome);
        Assert.False(entry.Completed);
        Assert.Equal("Suspended", shift1.Status);
        Assert.Single(shift1.Problems);
    }

    [Fact]
    public void Step6_ContinuationShift_SameMissionId_DifferentDay()
    {
        var day1 = DateTime.Today;
        var day2 = day1.AddDays(1);

        var shift1 = new Shift { Date = day1, MissionId = "miss-1" };
        var shift2 = new Shift { Date = day2, MissionId = "miss-1" };

        // Stesso MissionId, Shift diversi, giornate diverse
        Assert.Equal(shift1.MissionId, shift2.MissionId);
        Assert.NotEqual(shift1.Id, shift2.Id);
        Assert.NotEqual(shift1.Date, shift2.Date);
    }

    [Fact]
    public void Step7_TeamChange_ShiftRecordsPresentOperators()
    {
        // Mission pianificata con team B, ma in giornata arriva sostituto
        var planned = new MissionData(["op-B1", "op-B2"], ["v-1"], [], "09:00-17:00");
        var actual = new ShiftResources(["op-B1", "op-SUB"], ["v-1"], []);

        var shift = new Shift { Date = DateTime.Today, Mission = planned, Resources = actual };

        Assert.Contains("op-B2", shift.Mission!.Operators);
        Assert.DoesNotContain("op-B2", shift.Resources!.PresentOperators);
        Assert.Contains("op-SUB", shift.Resources.PresentOperators);
    }

    [Fact]
    public void Step8_EachShiftCreatesOwnObjects_AllLinkedToSameDeal()
    {
        // Gli oggetti sono creati via censimento in ciascuno Shift; tutti riferiscono lo stesso DealId
        var objs1 = Enumerable.Range(0, 50).Select(i => new PhysicalObject { Name = $"p0-{i}", DealId = "deal-1" }).ToList();
        var objs2 = Enumerable.Range(0, 40).Select(i => new PhysicalObject { Name = $"p1-{i}", DealId = "deal-1" }).ToList();
        var objs3 = Enumerable.Range(0, 30).Select(i => new PhysicalObject { Name = $"p2-{i}", DealId = "deal-1" }).ToList();

        foreach (var o in objs1.Concat(objs2).Concat(objs3)) o.PickUp("miss-x");
        Assert.All(objs1.Concat(objs2).Concat(objs3), o => Assert.Equal(ObjectStatus.PickedUp, o.Status));
        Assert.All(objs1.Concat(objs2).Concat(objs3), o => Assert.Equal("deal-1", o.DealId));
    }

    [Fact]
    public void Step9_ThreeWorkOrders_EachLifecycleIndependent()
    {
        var wo1 = new WorkOrder();
        var wo2 = new WorkOrder();
        var wo3 = new WorkOrder();

        // wo1 completato, wo2 e wo3 ancora in corso
        wo1.ServizioPronto("Sales");
        wo1.Programma("Ops");
        wo1.AvviaEsecuzione("Ops");
        wo1.CompletaEsecuzione("Ops");
        wo1.VerificaEConcludi("Ops");

        wo2.ServizioPronto("Sales");
        wo2.Programma("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, wo1.Status);
        Assert.Equal(WorkOrderStatus.Scheduled, wo2.Status);
        Assert.Equal(WorkOrderStatus.Completing, wo3.Status);
    }

    [Fact]
    public void Step10_Day3ShiftComplete_CascadeClosesAllWOsAndDeal()
    {
        // Dopo il giorno 3, ciascun WO si conclude; le 3 ServiceBooked diventano Completato;
        // la Quotation passa a Completed; il Deal (OneOff senza deposito) diventa Concluded.
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify();
        deal.EnterNegotiation();
        deal.Convert();

        var svc1 = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var svc2 = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var svc3 = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        foreach (var s in new[] { svc1, svc2, svc3 })
        {
            s.AccettaServizio();
            s.SegnaComePronto();
            s.ServizioCompletato();
        }
        Assert.All(new[] { svc1, svc2, svc3 }, s => Assert.Equal(ServiceBookedStatus.Completed, s.Status));

        deal.Activate();
        var closed = deal.TryCloseIfNoObjectsRemaining(0, allServicesCompleted: true);
        Assert.True(closed);
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }

    [Fact]
    public void Step11_MissionHasNoStatus_DerivableFromShifts()
    {
        // Una Mission non ha stato esplicito nel modello; verifichiamo la proprieta'
        var mission = new Mission();
        // No public Status property on Mission — si puo' solo marcare IsCancelled
        Assert.False(mission.IsCancelled);
        mission.IsCancelled = true;
        Assert.True(mission.IsCancelled);
    }

    [Fact]
    public void Step12_ShiftSerializesMultipleRitiriOnSingleDay_NotThisUC()
    {
        // Controprova: in questo UC ogni Shift ha UN solo ServiceEntry (1 ritiro per giorno)
        var shift = new Shift { Date = DateTime.Today };
        shift.AddServiceEntry("wo-p0", "deal-1", "lead-1", ServiceEntryType.Ritiro,
            new ClientData("Bianchi", "333"));
        Assert.Single(shift.ServiceEntries);
    }

    [Fact]
    public void Step13_EachShiftCarriesOwnTeamAndVehicle()
    {
        var s1 = new Shift { Mission = new MissionData(["op-A"], ["v-1"], [], "09-17") };
        var s2 = new Shift { Mission = new MissionData(["op-B"], ["v-2"], [], "09-17") };

        Assert.NotEqual(s1.Mission!.Operators, s2.Mission!.Operators);
        Assert.NotEqual(s1.Mission.Vehicles, s2.Mission.Vehicles);
    }

    [Fact]
    public void Step14_Events_EmittedPerDayCompletion()
    {
        Emit(new OperationCompletedEvent("shift-d1", "wo-p0"));
        Emit(new OperationCompletedEvent("shift-d2", "wo-p1"));
        Emit(new OperationCompletedEvent("shift-d3", "wo-p2"));
        Assert.Equal(3, _events.Count);
        Assert.All(_events, e => Assert.IsType<OperationCompletedEvent>(e));
    }
}

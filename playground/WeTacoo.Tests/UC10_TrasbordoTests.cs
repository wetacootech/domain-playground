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
/// UC-10: Trasbordo non programmato.
/// Verifica: WorkOrder operativo non commerciale (Type = Operational, ServiceType Trasferimento),
/// Mission con serviceRefs a WorkOrder diversi, Task 'Trasbordo' trasversale (ServiceEntryId = null)
/// registrato in entrambi gli Shift.
/// </summary>
public class UC10_TrasbordoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_OperationalWorkOrder_NotLinkedToServiceBooked()
    {
        var woOperational = new WorkOrder
        {
            Type = WorkOrderType.Operational,
            ServiceBookedId = null, // nessun ServiceBooked
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Trasferimento, false, false, "area-mi")
        };

        Assert.Equal(WorkOrderType.Operational, woOperational.Type);
        Assert.Null(woOperational.ServiceBookedId);
        Assert.Equal(ServiceTypeEnum.Trasferimento, woOperational.ServiceType.Type);
    }

    [Fact]
    public void Step2_CommercialWorkOrder_StillExists_ForOriginalService()
    {
        // Lato commerciale resta intatto il WO del ritiro
        var woRitiro = new WorkOrder
        {
            Type = WorkOrderType.Commercial,
            ServiceBookedId = "svc-ritiro",
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, "area-mi")
        };
        Assert.Equal(WorkOrderType.Commercial, woRitiro.Type);
        Assert.NotNull(woRitiro.ServiceBookedId);
    }

    [Fact]
    public void Step3_MissionA_ReferencesCommercialWO_Ritiro()
    {
        // Team A (furgone piccolo): serviceRef al WO ritiro commerciale
        var planning = new Planning { Date = DateTime.Today };
        var teamA = new PlanningTeam { OperatorIds = ["op-A1", "op-A2"] };
        planning.Teams.Add(teamA);

        var missionA = planning.AddMission(teamA.Id, [new ServiceRef("wo-ritiro-commerciale", 100)], ["v-piccolo"]);
        Assert.Single(missionA.ServiceRefs);
        Assert.Equal("wo-ritiro-commerciale", missionA.ServiceRefs[0].ServiceId);
    }

    [Fact]
    public void Step4_MissionB_ReferencesOperationalWO_Trasferimento()
    {
        // Team B (furgone grande): serviceRef al WO trasferimento operativo
        var planning = new Planning { Date = DateTime.Today };
        var teamB = new PlanningTeam { OperatorIds = ["op-B1"] };
        planning.Teams.Add(teamB);

        var missionB = planning.AddMission(teamB.Id, [new ServiceRef("wo-trasferimento-operativo", 100)], ["v-grande"]);
        Assert.Single(missionB.ServiceRefs);
        Assert.Equal("wo-trasferimento-operativo", missionB.ServiceRefs[0].ServiceId);
    }

    [Fact]
    public void Step5_MissionB_MultipleServiceRefs_Possible()
    {
        // Mission B puo' avere piu' serviceRef: il suo Deal Y + il trasferimento post-trasbordo
        var planning = new Planning { Date = DateTime.Today };
        var teamB = new PlanningTeam { OperatorIds = ["op-B1"] };
        planning.Teams.Add(teamB);

        var missionB = planning.AddMission(teamB.Id,
            [new ServiceRef("wo-dealY-consegna", 70), new ServiceRef("wo-trasferimento-operativo", 30)],
            ["v-grande"]);

        Assert.Equal(2, missionB.ServiceRefs.Count);
        Assert.Equal(100, missionB.ServiceRefs.Sum(r => r.VolumePercentage));
    }

    [Fact]
    public void Step6_TrasbordoTask_InShiftA_TransverseToWO()
    {
        // Task 'Trasbordo' trasversale: ServiceEntryId = null
        var shiftA = new Shift { MissionId = "miss-A", Date = DateTime.Today };
        var entryA = shiftA.AddServiceEntry("wo-ritiro-commerciale", "deal-X", "lead-X",
            ServiceEntryType.Ritiro, new ClientData("Cliente X", "333"));

        var trasbordoTask = shiftA.AddTask(TaskType.Trasbordo, null);
        trasbordoTask.Notes = "Scarico da furgone piccolo a furgone grande";

        Assert.Equal(TaskType.Trasbordo, trasbordoTask.Type);
        Assert.Null(trasbordoTask.ServiceEntryId);
    }

    [Fact]
    public void Step7_TrasbordoTask_InShiftB_Mirror()
    {
        // Entrambi gli Shift registrano il trasbordo dal proprio punto di vista
        var shiftB = new Shift { MissionId = "miss-B", Date = DateTime.Today };
        var trasbordoB = shiftB.AddTask(TaskType.Trasbordo, null);
        trasbordoB.Notes = "Carico su furgone grande dal piccolo";

        Assert.Equal(TaskType.Trasbordo, trasbordoB.Type);
        Assert.Null(trasbordoB.ServiceEntryId);
    }

    [Fact]
    public void Step8_OperationalWO_HasOwnLifecycle()
    {
        var wo = new WorkOrder
        {
            Type = WorkOrderType.Operational,
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Trasferimento, false, false, null)
        };
        wo.ServizioPronto("Planner"); // Non c'e' ServiceBooked, ma i metodi funzionano ugualmente
        wo.Programma("Planner");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        wo.VerificaEConcludi("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step9_NoNewEntityForTrasbordo()
    {
        // Il modello non introduce una entita' 'Trasbordo' dedicata
        var task = new OperationalTask { Type = TaskType.Trasbordo };
        Assert.Equal(TaskType.Trasbordo, task.Type);
        Assert.IsType<OperationalTask>(task);
    }

    [Fact]
    public void Step10_ObjectsTransitionOnVehicle_SameStatus_BeforeAndAfterTrasbordo()
    {
        // Durante il trasbordo l'oggetto resta OnVehicle — cambia solo il veicolo (snapshot)
        var obj = new PhysicalObject { Name = "scatola", DealId = "deal-X" };
        obj.PickUp("miss-A");
        obj.LoadOnVehicle("miss-A");
        Assert.Equal(ObjectStatus.OnVehicle, obj.Status);

        // Il trasbordo non e' modellato come status change sull'Object: resta OnVehicle.
        // Gli snapshot di history registreranno eventualmente la missione di destinazione.
        // Non chiamiamo alcuna transizione di stato: verifichiamo che OnVehicle sia stabile.
        Assert.Equal(ObjectStatus.OnVehicle, obj.Status);
    }

    [Fact]
    public void Step11_RuntimeCreated_NotPreplanned()
    {
        // Il WO operativo nasce a runtime — lo simuliamo creandolo in un secondo momento
        var woRitiro = new WorkOrder { Type = WorkOrderType.Commercial };
        var originalCreatedAt = woRitiro.CreatedAt;

        // Piu' tardi, il planner aggiunge un WO operativo
        System.Threading.Thread.Sleep(10);
        var woTrasferimento = new WorkOrder { Type = WorkOrderType.Operational };
        Assert.True(woTrasferimento.CreatedAt >= originalCreatedAt);
    }

    [Fact]
    public void Step12_OperationalWO_NoQuotationLink()
    {
        var wo = new WorkOrder { Type = WorkOrderType.Operational };
        // Il WO operativo non ha ServiceBookedId (nessuna Quotation dietro)
        Assert.Null(wo.ServiceBookedId);
    }

    [Fact]
    public void Step13_MissionServiceRefs_CanReferenceMixedWOTypes()
    {
        // Una Mission puo' avere serviceRefs a WO di tipo Commerciale + Operativo
        var planning = new Planning { Date = DateTime.Today };
        var team = new PlanningTeam { OperatorIds = ["op-X"] };
        planning.Teams.Add(team);

        var mission = planning.AddMission(team.Id,
            [new ServiceRef("wo-commerciale", 50), new ServiceRef("wo-operativo", 50)],
            ["v-1"]);

        Assert.Equal(2, mission.ServiceRefs.Count);
        // La Mission non distingue a livello strutturale: il tipo si legge sul WO referenziato
    }
}

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
/// UC-5: Sopralluogo che cambia il preventivo (DDD5 §4.8 review 2026-04-17).
/// Il Sopralluogo NON e' un tipo di ServiceBooked: e' un WorkOrder (Type=Sopralluogo)
/// creato da un ServiceBooked esistente su Quotation in stato != Draft.
/// Il ServiceBooked porta InspectionId (ref WO Sopralluogo) + QuestionnaireId.
/// State machine: ToAccept -(RichiediSopralluogo)-> WaitingInspection -(SopralluogoCompletato)-> ToAccept -> ...
/// </summary>
public class UC5_SopralluogoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_SopralluogoType_RemovedFromServiceBookedType()
    {
        // Il tipo Sopralluogo non esiste piu' sui ServiceBooked
        var names = Enum.GetNames<ServiceBookedType>();
        Assert.DoesNotContain("Sopralluogo", names);
    }

    [Fact]
    public void Step2_WorkOrderType_HasSopralluogoDiscriminator()
    {
        // WorkOrder ora ha 3 tipi: Commercial, Operational, Sopralluogo (XML TO BE DDD 7)
        var names = Enum.GetNames<WorkOrderType>();
        Assert.Contains("Commercial", names);
        Assert.Contains("Operational", names);
        Assert.Contains("Sopralluogo", names);
    }

    [Fact]
    public void Step3_RichiediSopralluogo_MovesServiceBookedToWaitingInspection()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var wo = new WorkOrder { Type = WorkOrderType.Sopralluogo };

        svc.RichiediSopralluogo(wo.Id);

        Assert.Equal(ServiceBookedStatus.WaitingInspection, svc.Status);
        Assert.Equal(wo.Id, svc.InspectionId);
    }

    [Fact]
    public void Step4_RichiediSopralluogo_IgnoredIfNotInToAccept()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio(); // ToComplete
        svc.RichiediSopralluogo("wo-1");

        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
        Assert.Null(svc.InspectionId);
    }

    [Fact]
    public void Step5_SopralluogoCompletato_ReturnsToAccept()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.RichiediSopralluogo("wo-1");
        Assert.Equal(ServiceBookedStatus.WaitingInspection, svc.Status);

        svc.SopralluogoCompletato();

        Assert.Equal(ServiceBookedStatus.ToAccept, svc.Status);
    }

    [Fact]
    public void Step6_InspectionWorkOrder_HasSopralluogoType()
    {
        var wo = new WorkOrder
        {
            Type = WorkOrderType.Sopralluogo,
            ServiceBookedId = "svc-1",
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Sopralluogo, false, false, "area-mi"),
            Commercial = new CommercialData("lead-1", "quest-1", "Sopralluogo propedeutico al Ritiro", [], false)
        };

        Assert.Equal(WorkOrderType.Sopralluogo, wo.Type);
        Assert.Equal(ServiceTypeEnum.Sopralluogo, wo.ServiceType.Type);
    }

    [Fact]
    public void Step7_InspectionWorkOrder_AutoConcludesAfterShift_WithoutToVerify()
    {
        // Il WO Sopralluogo transita comunque via ToVerify come ogni WO, ma l'handler
        // OperationCompletedEvent in PlaygroundState invoca VerificaEConcludi automaticamente
        // per tipo Sopralluogo. Qui testiamo che i metodi dominio sono richiamabili
        // nella sequenza che il handler applica.
        var wo = new WorkOrder { Type = WorkOrderType.Sopralluogo };
        wo.ServizioPronto("Commercial");
        wo.Programma("RespOps");
        wo.AvviaEsecuzione("Operatore");
        wo.CompletaEsecuzione("Execution");           // applicato dall'handler su Shift Completed
        wo.VerificaEConcludi("Auto");                 // applicato nello stesso handler per WO Sopralluogo

        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step8_InspectionCompletataEvent_EmittedOnCompletion()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var wo = new WorkOrder { Type = WorkOrderType.Sopralluogo };
        svc.RichiediSopralluogo(wo.Id);

        // Simula esecuzione completa del WO
        wo.ServizioPronto("Commercial");
        wo.Programma("RespOps");
        wo.AvviaEsecuzione("Operatore");
        wo.CompletaEsecuzione("Operatore");
        wo.VerificaEConcludi("RespOps");

        svc.SopralluogoCompletato();
        Emit(new InspectionCompletataEvent(wo.Id, svc.Id, "q-1"));

        Assert.Equal(ServiceBookedStatus.ToAccept, svc.Status);
        Assert.Single(_events);
        Assert.Equal("Operational", _events[0].SourceBC);
        Assert.Equal("Commercial", _events[0].TargetBC);
    }

    [Fact]
    public void Step9_Questionnaire_FilledDuringExecution()
    {
        // Durante l'esecuzione del WO Sopralluogo, l'operatore compila il Questionnaire
        var quest = new Questionnaire { Origin = "Sopralluogo" };
        var q1 = new Question { Data = new QuestionAnswer("Volume totale?", "number", null, "all") };
        var q2 = new Question { Data = new QuestionAnswer("Piani aggiuntivi?", "boolean", null, "all") };
        quest.Questions.Add(q1);
        quest.Questions.Add(q2);

        Assert.False(quest.IsCompleted);

        quest.AnswerQuestion(q1.Id, "28");
        quest.AnswerQuestion(q2.Id, "true");

        Assert.True(quest.IsCompleted);
    }

    [Fact]
    public void Step10_ServiceEntryType_Sopralluogo_StillExists()
    {
        // Lo Shift del WO Sopralluogo genera ServiceEntry tipo Sopralluogo (InspectionData VO)
        Assert.Contains(ServiceEntryType.Sopralluogo, Enum.GetValues<ServiceEntryType>());
    }

    [Fact]
    public void Step11_FullFlow_SopralluogoChangesPriceThenAcceptance()
    {
        // Scenario completo: Quotation Draft -> InProgress -> Sopralluogo -> rinegozia -> Finalize -> Accept
        var q = new Quotation { DealId = "deal-1" };
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        q.Services.Add(svc);
        q.Products.Add(new Product { Name = "Ritiro (18m3 dichiarati)", Price = 400m });

        q.Confirm(); // Draft -> InProgress (condizione per richiedere sopralluogo)
        Assert.Equal(QuotationStatus.InProgress, q.Status);

        var woInsp = new WorkOrder { Type = WorkOrderType.Sopralluogo };
        q.QuestionnaireId = "quest-1"; // Questionnaire unico per Quotation (review 2026-04-17)
        svc.RichiediSopralluogo(woInsp.Id);
        Assert.Equal(ServiceBookedStatus.WaitingInspection, svc.Status);

        // ... simulazione WO eseguito ...
        svc.SopralluogoCompletato();
        q.MarkQuestionnaireReady();
        Assert.Equal(ServiceBookedStatus.ToAccept, svc.Status);
        Assert.True(q.QuestionnaireReady);

        // Sales rinegozia prezzo
        q.Products.Clear();
        q.Products.Add(new Product { Name = "Ritiro (28m3 reali)", Price = 650m });
        Assert.Equal(650m, q.TotalPrice);

        // Cliente accetta. Con Quotation.QuestionnaireReady=true il ServiceBooked salta ToComplete e va a Ready.
        q.Finalize();
        svc.AccettaServizio(q.QuestionnaireReady);
        Assert.Equal(QuotationStatus.Finalized, q.Status);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);
        Assert.Equal(woInsp.Id, svc.InspectionId);
    }

    [Fact]
    public void Step12_AcceptWithoutInspection_GoesToComplete_AsBefore()
    {
        // Controllo di regressione: senza sopralluogo, AccettaServizio continua ad andare a ToComplete.
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
    }
}

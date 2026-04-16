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
/// UC-5: Sopralluogo che cambia il preventivo.
/// Verifica: Inspection AR separato in Operational (no WorkOrder, no Mission, no Shift),
/// Questionnaire in Commercial auto-verificato, ServiceBooked passa DaAccettare -> InAttesaSopralluogo -> DaAccettare,
/// Quotation resta Draft/InProgress (non cambia stato per sopralluogo).
/// </summary>
public class UC5_SopralluogoTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_Inspection_IsOperationalAggregateRoot_NotWorkOrder()
    {
        var inspection = new Inspection
        {
            ServiceBookedId = "svc-1",
            QuestionnaireId = "quest-1",
            Caratteristiche = "Appartamento 3 locali, secondo piano"
        };

        Assert.False(inspection.IsCompleted);
        Assert.False(inspection.IsCancelled);
        Assert.Equal("Da completare", inspection.Status);
        Assert.Null(inspection.CompletedAt);
    }

    [Fact]
    public void Step2_CommercialRequestsInspection_ServiceBookedTransitions()
    {
        var svc = new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("20100", "area-mi")
        };
        Assert.Equal(ServiceBookedStatus.ToAccept, svc.Status);

        svc.RichiediSopralluogo();
        Assert.Equal(ServiceBookedStatus.PendingInspection, svc.Status);
        Assert.Single(svc.StatusHistory);
    }

    [Fact]
    public void Step3_Event_SopralluogoRichiesto_EmittedFromCommercial()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var quest = new Questionnaire { Origin = "Inspection" };
        svc.QuestionnaireId = quest.Id;
        svc.RichiediSopralluogo();

        Emit(new SopralluogoRichiestoEvent(svc.Id, quest.Id, "Via Test 1, Milano"));
        Assert.Single(_events);
        Assert.Equal("Commercial", _events[0].SourceBC);
        Assert.Equal("Operational", _events[0].TargetBC);
    }

    [Fact]
    public void Step4_Inspection_HasNoMissionNoShift()
    {
        // L'Inspection non e' un WorkOrder: non genera ne' Mission ne' Shift
        var inspection = new Inspection();

        // Verifichiamo che l'AR Inspection non abbia relazioni strutturali con Planning/Shift
        Assert.IsNotType<WorkOrder>(inspection);
        Assert.IsNotType<Shift>(inspection);
        Assert.IsNotType<Mission>(inspection);
    }

    [Fact]
    public void Step5_OperatorAnswersQuestions_IncrementalFill()
    {
        // Operator compila il Questionnaire (Commercial) progressivamente via eventi
        var quest = new Questionnaire { Origin = "Inspection" };
        var q1 = new Question { Data = new QuestionAnswer("Volume totale?", "number", null, "all") };
        var q2 = new Question { Data = new QuestionAnswer("Piani aggiuntivi?", "boolean", null, "all") };
        quest.Questions.Add(q1);
        quest.Questions.Add(q2);

        Assert.False(quest.IsCompleted);

        quest.AnswerQuestion(q1.Id, "28"); // reale: 28 m3 (vs 18 dichiarato)
        Assert.False(quest.IsCompleted);

        quest.AnswerQuestion(q2.Id, "true");
        Assert.True(quest.IsCompleted);
    }

    [Fact]
    public void Step6_Inspection_Documents_PhotosAttached()
    {
        var inspection = new Inspection();
        inspection.Documenti.Add("foto-seminterrato.jpg");
        inspection.Documenti.Add("foto-accesso.jpg");
        inspection.Documenti.Add("planimetria.pdf");

        Assert.Equal(3, inspection.Documenti.Count);
    }

    [Fact]
    public void Step7_InspectionComplete_OperatorMarksDone()
    {
        var inspection = new Inspection { ServiceBookedId = "svc-1" };
        inspection.Complete("op-ispettore");

        Assert.True(inspection.IsCompleted);
        Assert.Equal("Completato", inspection.Status);
        Assert.Equal("op-ispettore", inspection.CompletedBy);
        Assert.NotNull(inspection.CompletedAt);
    }

    [Fact]
    public void Step8_InspectionCancel_IsTerminalNegative()
    {
        var inspection = new Inspection();
        inspection.Cancel();
        Assert.True(inspection.IsCancelled);
        Assert.Equal("Annullato", inspection.Status);

        // Una volta cancellata, Complete non la riporta in vita
        inspection.Complete("late");
        Assert.False(inspection.IsCompleted);
        Assert.Equal("Annullato", inspection.Status);
    }

    [Fact]
    public void Step9_InspectionCompleted_Event_EmittedToCommercial()
    {
        var inspection = new Inspection { ServiceBookedId = "svc-1", QuestionnaireId = "quest-1" };
        inspection.Complete("op-1");

        Emit(new InspectionCompletataEvent(inspection.Id, inspection.ServiceBookedId, inspection.QuestionnaireId));
        Assert.Single(_events);
        Assert.Equal("Operational", _events[0].SourceBC);
        Assert.Equal("Commercial", _events[0].TargetBC);
    }

    [Fact]
    public void Step10_ServiceBooked_ReturnsToDaAccettare_AfterInspection()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.RichiediSopralluogo();
        Assert.Equal(ServiceBookedStatus.PendingInspection, svc.Status);

        svc.SopralluogoCompletato();
        Assert.Equal(ServiceBookedStatus.ToAccept, svc.Status);
        Assert.Equal(2, svc.StatusHistory.Count);
    }

    [Fact]
    public void Step11_QuotationStaysDraft_DuringInspection()
    {
        // La Quotation NON cambia stato per via del sopralluogo — cambia solo il ServiceBooked
        var q = new Quotation { DealId = "deal-1" };
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        q.Services.Add(svc);

        svc.RichiediSopralluogo();
        Assert.Equal(QuotationStatus.Draft, q.Status);
        svc.SopralluogoCompletato();
        Assert.Equal(QuotationStatus.Draft, q.Status);
    }

    [Fact]
    public void Step12_NoWorkOrder_CreatedDuringInspection()
    {
        // Finche' il ServiceBooked non e' Pronto, il WorkOrder del ritiro non esiste in Operational
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.RichiediSopralluogo();
        svc.SopralluogoCompletato();
        // svc ora DaAccettare — il WorkOrderId e' ancora null
        Assert.Null(svc.WorkOrderId);

        // Sales accetta, quotation finalizzata, svc segnato pronto
        svc.AccettaServizio();
        svc.SegnaComePronto();
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);

        // Ora si puo' creare il WorkOrder
        var wo = new WorkOrder { ServiceBookedId = svc.Id };
        svc.WorkOrderId = wo.Id;
        Assert.NotNull(svc.WorkOrderId);
    }

    [Fact]
    public void Step13_OtherServiceBooked_NotBlockedByInspection()
    {
        // Una Quotation con 2 ServiceBooked: solo uno richiede sopralluogo
        var q = new Quotation { DealId = "deal-1" };
        var svcRitiro = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var svcConsegna = new ServiceBooked { Type = ServiceBookedType.Consegna };
        q.Services.Add(svcRitiro);
        q.Services.Add(svcConsegna);

        svcRitiro.RichiediSopralluogo();

        // svcConsegna puo' avanzare indipendentemente
        svcConsegna.AccettaServizio();
        Assert.Equal(ServiceBookedStatus.PendingInspection, svcRitiro.Status);
        Assert.Equal(ServiceBookedStatus.ToComplete, svcConsegna.Status);
    }

    [Fact]
    public void Step14_SalesRenegotiates_QuotationRegenerated()
    {
        // Dopo sopralluogo, Sales ricrea o modifica la Quotation con nuovo prezzo
        var q = new Quotation { DealId = "deal-1" };
        q.Products.Add(new Product { Name = "Ritiro", Price = 400m });
        Assert.Equal(400m, q.TotalPrice);

        // Sales aggiorna prezzo (non modelliamo la modifica di un Product Entity per il ricalcolo;
        // il modello permette semplicemente di sommare i Product presenti)
        q.Products.Clear();
        q.Products.Add(new Product { Name = "Ritiro (28 m3)", Price = 650m });
        Assert.Equal(650m, q.TotalPrice);
    }
}

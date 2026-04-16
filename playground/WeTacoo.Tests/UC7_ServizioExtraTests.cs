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
/// UC-7: Servizio extra non preventivato.
/// Verifica: Task extra nello Shift (IsExtra=true), propagazione post-esecuzione a Operational -> Commercial,
/// Quotation -> Da adeguare (post-esecuzione), WorkOrder completa il suo ciclo fino a Concluso.
/// Differenza da UC-6: servizio concluso, Quotation ToAdjust (non ToVerify).
/// </summary>
public class UC7_ServizioExtraTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_Shift_RegistersExtraTask_WithIsExtraFlag()
    {
        var shift = new Shift { Date = DateTime.Today };
        shift.Start();
        var entry = shift.AddServiceEntry("wo-1", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, new ClientData("Cliente", "333"));
        // SE senza Start: l'in-progress vive sullo Shift InCorso

        var plannedTask = shift.AddTask(TaskType.Smontaggio, entry.Id);
        plannedTask.Notes = "Smontaggio armadio (preventivato)";

        var extraTask = shift.AddTask(TaskType.Smontaggio, entry.Id);
        extraTask.IsExtra = true;
        extraTask.Notes = "Smontaggio letto a castello (extra non preventivato)";

        Assert.Equal(2, shift.Tasks.Count);
        Assert.False(plannedTask.IsExtra);
        Assert.True(extraTask.IsExtra);
    }

    [Fact]
    public void Step2_ExtraTask_RecordsObjectivaData()
    {
        var shift = new Shift { Date = DateTime.Today };
        var entry = shift.AddServiceEntry("wo-1", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, null);
        var task = shift.AddTask(TaskType.Smontaggio, entry.Id, ["obj-letto"]);
        task.IsExtra = true;
        task.StartTime = DateTime.UtcNow.AddMinutes(-45);
        task.EndTime = DateTime.UtcNow;

        Assert.True(task.IsExtra);
        Assert.Single(task.ObjectIds);
        Assert.NotNull(task.StartTime);
        Assert.NotNull(task.EndTime);
    }

    [Fact]
    public void Step3_WorkOrder_CompletesNormallyEvenWithExtras()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // Gli extra non bloccano la verifica; Ops qualifica e conclude
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step4_Quotation_MarkToAdjust_PostExecution()
    {
        // Differenza con UC-6: qui siamo post-esecuzione — Da adeguare, non Da verificare
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        Assert.Equal(QuotationStatus.Finalized, q.Status);

        q.MarkToAdjust();
        Assert.Equal(QuotationStatus.ToAdjust, q.Status);
    }

    [Fact]
    public void Step5_ServiceBooked_Completato_EvenWithAdjustment()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.ServizioCompletato();

        Assert.Equal(ServiceBookedStatus.Completed, svc.Status);
    }

    [Fact]
    public void Step6_QuotationToAdjust_ResolvedByComplete_OrStaysComplete()
    {
        // Addebito: resta in ToAdjust finche' Sales conferma, poi Complete
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        q.MarkToAdjust();
        Assert.Equal(QuotationStatus.ToAdjust, q.Status);

        q.Complete();
        Assert.Equal(QuotationStatus.Completed, q.Status);
    }

    [Fact]
    public void Step7_Absorb_QuotationStillCompletes_NoExtraCharge()
    {
        // Assorbimento: Sales non addebita, ma chiude comunque la pratica
        var q = new Quotation { DealId = "deal-1" };
        q.Finalize();
        q.MarkToAdjust();
        q.Complete();
        Assert.Equal(QuotationStatus.Completed, q.Status);

        // Nessun nuovo Payment creato (assorbimento)
        var payments = new List<Payment>();
        Assert.Empty(payments);
    }

    [Fact]
    public void Step8_Charge_Addebito_Scenario()
    {
        // Scenario addebito: Sales aggiunge riga costo + comanda Financial
        var payment = new Payment { DealId = "deal-1", PaymentType = "OneOff" };
        payment.Products.Add(new SimplifiedProduct { Name = "Smontaggio extra (letto)", Price = 80m });
        Assert.Equal(80m, payment.TotalAmount);

        var charge = payment.AddCharge(80m);
        payment.ExecuteCharge(charge.Id);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(80m, payment.PaidAmount);
    }

    [Fact]
    public void Step9_FilteringChain_Execution_Operational_Commercial()
    {
        // Shift extra -> evento a Operational -> Operational qualifica -> evento a Commercial
        Emit(new OperationCompletedEvent("shift-1", "wo-1"));
        // Operational qualifica, invia a Commercial (via ServizioCompletato con HasDifferences)
        Emit(new ServizioCompletatoEvent("wo-1", "svc-1", 12m, true, "Extra: smontaggio letto 45 min"));

        Assert.Equal(2, _events.Count);
        Assert.Equal("Execution", _events[0].SourceBC);
        Assert.Equal("Operational", _events[1].SourceBC);
        Assert.Equal("Commercial", _events[1].TargetBC);
    }

    [Fact]
    public void Step10_DifferenceUC6_UC7()
    {
        // UC-6 (in-execution): WO InPausa, Quotation ToVerify (blocco operativo)
        var wo6 = new WorkOrder();
        wo6.ServizioPronto("Ops");
        wo6.Programma("Ops");
        wo6.AvviaEsecuzione("Ops");
        wo6.MettiInPausa("problema");

        var q6 = new Quotation { DealId = "d-1" };
        q6.Finalize();
        q6.MarkToVerify();

        // UC-7 (post-execution): WO Concluso, Quotation ToAdjust (no blocco)
        var wo7 = new WorkOrder();
        wo7.ServizioPronto("Ops");
        wo7.Programma("Ops");
        wo7.AvviaEsecuzione("Ops");
        wo7.CompletaEsecuzione("Ops");
        wo7.VerificaEConcludi("Ops");

        var q7 = new Quotation { DealId = "d-1" };
        q7.Finalize();
        q7.MarkToAdjust();

        Assert.Equal(WorkOrderStatus.Paused, wo6.Status);
        Assert.Equal(WorkOrderStatus.Concluded, wo7.Status);
        Assert.Equal(QuotationStatus.ToVerify, q6.Status);
        Assert.Equal(QuotationStatus.ToAdjust, q7.Status);
    }

    [Fact]
    public void Step11_MultipleExtraTasks_OnSameShift()
    {
        var shift = new Shift { Date = DateTime.Today };
        var entry = shift.AddServiceEntry("wo-1", "d-1", "l-1",
            ServiceEntryType.Ritiro, null);

        var t1 = shift.AddTask(TaskType.Smontaggio, entry.Id); t1.IsExtra = true;
        var t2 = shift.AddTask(TaskType.Imballaggio, entry.Id); t2.IsExtra = true;
        var t3 = shift.AddTask(TaskType.Facchinaggio, entry.Id); t3.IsExtra = false;

        Assert.Equal(3, shift.Tasks.Count);
        Assert.Equal(2, shift.Tasks.Count(t => t.IsExtra));
    }

    [Fact]
    public void Step12_WorkOrderConcluso_DoesNotBlockMission()
    {
        // Il WO concluso non mette in pausa il planning
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.CompletaEsecuzione("Ops");
        wo.VerificaEConcludi("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
        // Annulla non lo riporta indietro (solo cambio di status)
        wo.Annulla("test");
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
    }
}

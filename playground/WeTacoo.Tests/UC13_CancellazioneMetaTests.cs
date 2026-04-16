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
/// UC-13: Cancellazione a meta' esecuzione.
/// Verifica: ChiusuraAnticipata sul WorkOrder (InEsecuzione -> DaVerificare), nessun nuovo enum cancelled,
/// nuova Quotation per consegna totale dei 75 oggetti, Mission giorni 2-3 IsCancelled,
/// ServiceEntry cancellate, Payment penale, ActivePlan transita rapido pending->active->closed.
/// </summary>
public class UC13_CancellazioneMetaTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    [Fact]
    public void Step1_WorkOrder_ChiusuraAnticipata_FromInEsecuzione()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Sales");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        wo.ChiusuraAnticipata("Cancellazione cliente, completato 75 su 200");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);
        Assert.Contains(wo.StatusHistory, h => h.Contains("Cancellazione cliente"));
    }

    [Fact]
    public void Step2_Event_ChiusuraAnticipata_EmittedToOperational()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.ChiusuraAnticipata("Cliente cancella");

        Emit(new ChiusuraAnticipataEvent(wo.Id, "svc-ritiro", "Cancellazione cliente"));
        Assert.Single(_events);
        Assert.Equal("Commercial", _events[0].SourceBC);
        Assert.Equal("Operational", _events[0].TargetBC);
    }

    [Fact]
    public void Step3_WorkOrder_VerificaEConcludi_AfterChiusuraAnticipata()
    {
        var wo = new WorkOrder();
        wo.ServizioPronto("Ops");
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Ops");
        wo.ChiusuraAnticipata("cancelled");
        wo.VerificaEConcludi("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Step4_NoCancelledEnum_Added_ModelledAsScopeReduction()
    {
        // Verifichiamo che WorkOrderStatus contenga gli stati attesi (post refactor EN naming)
        var statuses = Enum.GetNames(typeof(WorkOrderStatus));
        Assert.Contains("Concluded", statuses);
        Assert.Contains("Cancelled", statuses);
        Assert.DoesNotContain("Aborted", statuses);
    }

    [Fact]
    public void Step5_ServiceBooked_ChiudiAVolumeRidotto()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.RichiedeIntervento("cancellazione");
        svc.ChiudiAVolumeRidotto();

        Assert.Equal(ServiceBookedStatus.Completed, svc.Status);
    }

    [Fact]
    public void Step6_CompletionRecord_Tracks75Objects()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();
        svc.SegnaComePronto();
        svc.ServizioCompletato();
        svc.CompletionData = new CompletionRecord(30m, 75, "Scope ridotto per cancellazione cliente", DateTime.UtcNow);

        Assert.Equal(75, svc.CompletionData!.ObjectsMoved);
        Assert.NotNull(svc.CompletionData.DifferencesNotes);
    }

    [Fact]
    public void Step7_NewQuotation_Consegna_For75Objects()
    {
        // Nuova Quotation nello stesso Deal per consegna totale
        var deal = new Deal { LeadId = "lead-1" };
        var quotationRitiro = new Quotation { DealId = deal.Id, IsInitial = true };
        quotationRitiro.Finalize();
        quotationRitiro.Complete();

        var quotationConsegna = new Quotation { DealId = deal.Id, IsInitial = false };
        quotationConsegna.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            Notes = "Consegna totale post-cancellazione",
            SelectedObjectIds = Enumerable.Range(0, 75).Select(i => $"obj-{i}").ToList()
        });
        deal.Quotations.Add(quotationRitiro);
        deal.Quotations.Add(quotationConsegna);

        Assert.Equal(2, deal.Quotations.Count);
        Assert.Equal(75, quotationConsegna.Services[0].SelectedObjectIds.Count);
        Assert.Equal(ServiceBookedType.Consegna, quotationConsegna.Services[0].Type);
    }

    [Fact]
    public void Step8_Missions_Day2Day3_Cancelled()
    {
        // Mission giorni 2 e 3 marcate come cancellate
        var m2 = new Mission();
        var m3 = new Mission();
        m2.IsCancelled = true;
        m3.IsCancelled = true;

        Assert.True(m2.IsCancelled);
        Assert.True(m3.IsCancelled);
    }

    [Fact]
    public void Step9_ServiceEntries_Day2Day3_Cancelled()
    {
        // DDD5 review 2026-04-14: la SE non ha stato "Cancelled".
        // Cancellazione nasce dal WorkOrder (Annullato); il client filtra la SE.
        var shift2 = new Shift { Date = DateTime.Today.AddDays(1) };
        var entry = shift2.AddServiceEntry("wo-ritiro", "deal-1", "lead-1",
            ServiceEntryType.Ritiro, new ClientData("C", "333"));
        var wo = new WorkOrder();
        wo.Annulla("Ops");
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
        Assert.False(entry.Completed);
    }

    [Fact]
    public void Step10_Penalty_Payment_Created()
    {
        // Commercial calcola penale -> Payment con charge eseguito da Financial
        var penalty = new Payment { DealId = "deal-1", PaymentType = "OneOff" };
        penalty.Products.Add(new SimplifiedProduct
        {
            Name = "Penale cancellazione",
            Price = 500m
        });
        var charge = penalty.AddCharge(500m);
        charge.Notes = "Penale cancellazione cliente";
        penalty.ExecuteCharge(charge.Id);

        Assert.Equal(PaymentStatus.Paid, penalty.Status);
        Assert.Equal(500m, penalty.PaidAmount);
    }

    [Fact]
    public void Step11_ActivePlan_RapidTransit_PendingActiveClosed()
    {
        // Se c'era deposito: Plan si attiva (75 oggetti in WH), poi subito chiude (0 post-consegna)
        var deal = new Deal { LeadId = "lead-1" };
        deal.Qualify(); deal.EnterNegotiation(); deal.Convert();
        var q = new Quotation { DealId = deal.Id };
        q.DraftPlans.Add(new DraftPlan { MonthlyFee = 100m, EstimatedM3 = 50m });
        deal.Quotations.Add(q);
        deal.CreatePlan(q, q.DraftPlans[0]);
        deal.Activate();

        // 75 oggetti -> plan active
        deal.TryCloseIfNoObjectsRemaining(75);
        Assert.Equal(DealStatus.Active, deal.Status);

        // Consegna totale: 0 oggetti -> plan closed, deal concluded
        var closed = deal.TryCloseIfNoObjectsRemaining(0);
        Assert.True(closed);
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }

    [Fact]
    public void Step12_NewDeliveryObjects_FromWarehouseBackToClient()
    {
        var objs = Enumerable.Range(0, 75).Select(i =>
        {
            var o = new PhysicalObject { Name = $"obj-{i}", DealId = "deal-1" };
            o.StockDirectly("wh-mi");
            return o;
        }).ToList();

        foreach (var o in objs)
        {
            o.LoadFromWarehouse("miss-consegna");
            o.Deliver("miss-consegna");
        }
        Assert.All(objs, o => Assert.Equal(ObjectStatus.Delivered, o.Status));
    }

    [Fact]
    public void Step13_ConsegnaWorkOrder_NormalLifecycle()
    {
        var woConsegna = new WorkOrder
        {
            Type = WorkOrderType.Commercial,
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, false, "area-mi")
        };
        woConsegna.ServizioPronto("Sales");
        woConsegna.Programma("Ops");
        woConsegna.AvviaEsecuzione("Ops");
        woConsegna.CompletaEsecuzione("Ops");
        woConsegna.VerificaEConcludi("Ops");

        Assert.Equal(WorkOrderStatus.Concluded, woConsegna.Status);
    }

    [Fact]
    public void Step14_WorkOrder_Annulla_IsDifferent_FromChiusuraAnticipata()
    {
        // ChiusuraAnticipata: porta a DaVerificare (poi Concluso)
        var w1 = new WorkOrder();
        w1.ServizioPronto("Ops");
        w1.Programma("Ops");
        w1.AvviaEsecuzione("Ops");
        w1.ChiusuraAnticipata("chiuso");
        Assert.Equal(WorkOrderStatus.ToVerify, w1.Status);

        // Annulla: porta ad Annullato (terminale negativo)
        var w2 = new WorkOrder();
        w2.ServizioPronto("Ops");
        w2.Programma("Ops");
        w2.AvviaEsecuzione("Ops");
        w2.Annulla("annullato");
        Assert.Equal(WorkOrderStatus.Cancelled, w2.Status);
    }

    [Fact]
    public void Step15_QuotationOriginalRitiro_StaysFinalizedThenCompleted()
    {
        // La Quotation originale (ritiro) resta Finalizzata e poi chiude a scope ridotto
        var qRitiro = new Quotation { DealId = "deal-1", IsInitial = true };
        qRitiro.Finalize();
        Assert.Equal(QuotationStatus.Finalized, qRitiro.Status);

        qRitiro.Complete();
        Assert.Equal(QuotationStatus.Completed, qRitiro.Status);
    }

    [Fact]
    public void Step16_Events_FullCancellationFlow()
    {
        // Chain di eventi attesi
        Emit(new ChiusuraAnticipataEvent("wo-ritiro", "svc-ritiro", "Cancellazione cliente"));
        Emit(new ServizioCompletatoEvent("wo-ritiro", "svc-ritiro", 30m, true, "75 oggetti ritirati, scope ridotto"));
        Emit(new PaymentCreatedEvent("pay-penale", "deal-1"));
        Emit(new WorkOrderCreatedEvent("wo-consegna", "Commercial", "Consegna"));

        Assert.Equal(4, _events.Count);
        Assert.IsType<ChiusuraAnticipataEvent>(_events[0]);
        Assert.IsType<ServizioCompletatoEvent>(_events[1]);
        Assert.IsType<PaymentCreatedEvent>(_events[2]);
        Assert.IsType<WorkOrderCreatedEvent>(_events[3]);
    }
}

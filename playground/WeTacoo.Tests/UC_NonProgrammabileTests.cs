using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Operational;
using WeTacoo.Domain.Operational.Enums;
using WeTacoo.Domain.Events;

namespace WeTacoo.Tests;

/// <summary>
/// UC: Non programmabile — il planner non riesce a collocare un WorkOrder in un giorno/slot
/// e lo rimanda a Commercial per decisione (§10d Caso 4). Commercial puo':
///   (A) risolvere con Riprogramma + nota al cliente (giorni extra, pagamento supplementare, condizioni nuove)
///   (B) annullare il servizio vecchio (SB + WO → Cancelled)
///
/// Stati coinvolti:
///   WO: ToSchedule → Paused (via NonProgrammabile) → ToSchedule (Riprogramma) o Cancelled (Annulla)
///   SB: Ready      → ToComplete                   → Ready                     o Cancelled
/// Coppia invariante: WO.Paused ↔ SB.ToComplete (§10d — RichiedeInterventoEvent handler).
/// </summary>
public class UC_NonProgrammabileTests
{
    private readonly List<IDomainEvent> _events = [];
    private void Emit(IDomainEvent evt) => _events.Add(evt);

    private static (ServiceBooked svc, WorkOrder wo) GiveReadyAndToSchedule()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AccettaServizio();      // ToAccept -> ToComplete
        svc.SegnaComePronto();      // ToComplete -> Ready

        var wo = new WorkOrder { Type = WorkOrderType.Commercial, ServiceBookedId = svc.Id };
        wo.ServizioPronto("Commercial");  // Completing -> ToSchedule
        svc.WorkOrderId = wo.Id;
        return (svc, wo);
    }

    [Fact]
    public void NonProgrammabile_FromToSchedule_MovesWoToPaused()
    {
        var (_, wo) = GiveReadyAndToSchedule();
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        wo.NonProgrammabile("Slot pieni, serve nuovo giorno");

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
    }

    [Fact]
    public void NonProgrammabile_CascadesServiceBookedToToComplete_ViaRichiedeIntervento()
    {
        // Simula il handler RichiedeInterventoEvent in PlaygroundState: quando Ops segnala
        // il problema, SB Ready -> ToComplete.
        var (svc, wo) = GiveReadyAndToSchedule();

        wo.NonProgrammabile("Risorse insufficienti quel giorno");
        Emit(new RichiedeInterventoEvent(wo.Id, svc.Id, "Non programmabile"));
        svc.RichiedeIntervento("Non programmabile");

        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.Single(_events);
    }

    [Fact]
    public void Resolve_Riprogramma_ReturnsToReadyAndToSchedule_WithNote()
    {
        // Commercial risolve con Riprogramma e nota al cliente (giorni extra + pagamento supplementare)
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.NonProgrammabile("Giorno richiesto saturo");
        svc.RichiedeIntervento("Non programmabile");

        var note = "Cliente accetta giorno successivo + supplemento 50 EUR";
        wo.InterventoRisoltoRiprogramma(note);
        svc.InterventoRisolto();
        Emit(new InterventoRisoltoEvent(wo.Id, "Riprogramma", note));

        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);
        Assert.Single(_events);
        Assert.Contains("Riprogramma", _events[0].Description);
    }

    [Fact]
    public void Resolve_Annulla_CascadesToCancelledOnBoth()
    {
        // Commercial decide di annullare il servizio vecchio (invece di riprogrammare)
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.NonProgrammabile("Non c'e' margine nel mese");
        svc.RichiedeIntervento("Non programmabile");

        svc.Annulla();
        wo.Annulla("Commercial");
        Emit(new ServizioCancellatoEvent(svc.Id, wo.Id));

        Assert.Equal(ServiceBookedStatus.Cancelled, svc.Status);
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
        Assert.Single(_events);
    }

    [Fact]
    public void StateHistory_RecordsTransitions()
    {
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.NonProgrammabile("Slot saturi");
        svc.RichiedeIntervento("Non programmabile");

        wo.InterventoRisoltoRiprogramma("Giorno alternativo");
        svc.InterventoRisolto();

        Assert.Contains(wo.StatusHistory, h => h.Contains("ToSchedule") && h.Contains("Paused"));
        Assert.Contains(wo.StatusHistory, h => h.Contains("Paused") && h.Contains("ToSchedule"));
        Assert.Contains(svc.StatusHistory, h => h.Contains("Ready") && h.Contains("ToComplete"));
        Assert.Contains(svc.StatusHistory, h => h.Contains("ToComplete") && h.Contains("Ready"));
    }

    [Fact]
    public void ToSchedule_AlsoReachableViaInterventoRisoltoRiprogramma_CompleteCycle()
    {
        // Ciclo completo: ToSchedule → Paused → ToSchedule (con Riprogramma) → Scheduled
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.NonProgrammabile("Slot pieni");
        svc.RichiedeIntervento("Non programmabile");

        wo.InterventoRisoltoRiprogramma("Nuovo giorno + supplemento");
        svc.InterventoRisolto();
        wo.Programma("Planner");

        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);
    }

    // ── Nuovo flusso review 2026-04-17: Ops sceglie destinatario (interno Ops vs escalation Commercial)

    [Fact]
    public void NonProgrammabile_ForCommercial_SetsFlagAndEscalates()
    {
        var (svc, wo) = GiveReadyAndToSchedule();

        wo.NonProgrammabile("Cliente chiede supplemento", forCommercial: true);
        // Simula il handler: emette RichiedeInterventoEvent -> SB ToComplete
        svc.RichiedeIntervento("Non programmabile");

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.True(wo.PausedForCommercial);
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
    }

    [Fact]
    public void NonProgrammabile_ForOpsInternal_DoesNotTouchServiceBooked()
    {
        // Gestione interna Ops: SB resta Ready, PausedForCommercial=false, nessun evento cross-BC
        var (svc, wo) = GiveReadyAndToSchedule();

        wo.NonProgrammabile("Slot saturi, riprogrammo con altro veicolo", forCommercial: false);

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.False(wo.PausedForCommercial);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status); // invariato!
    }

    [Fact]
    public void MettiInPausa_InExecution_ForCommercial()
    {
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.Programma("Planner"); wo.AvviaEsecuzione("Team");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        wo.MettiInPausa("Volume reale diverge, serve supplemento", forCommercial: true);
        svc.RichiedeIntervento("Problema in esecuzione");

        Assert.True(wo.PausedForCommercial);
        Assert.Equal(ServiceBookedStatus.ToComplete, svc.Status);
    }

    [Fact]
    public void MettiInPausa_InExecution_ForOpsInternal_SBUnchanged()
    {
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.Programma("Planner"); wo.AvviaEsecuzione("Team");

        wo.MettiInPausa("Veicolo ko, riassegno", forCommercial: false);

        Assert.Equal(WorkOrderStatus.Paused, wo.Status);
        Assert.False(wo.PausedForCommercial);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);
    }

    [Fact]
    public void CommercialRiprende_MovesWoInExecution_AndSBReady()
    {
        // Review 2026-04-17 opzione (a): Riprendi da Commercial porta WO -> InExecution (era InExecution prima della pausa)
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.Programma("Planner"); wo.AvviaEsecuzione("Team");
        wo.MettiInPausa("Problema commerciale", forCommercial: true);
        svc.RichiedeIntervento("Problema");

        wo.InterventoRisoltoRiprendi("Cliente approva supplemento");
        svc.InterventoRisolto();

        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);
        Assert.False(wo.PausedForCommercial);
    }

    [Fact]
    public void OpsInternal_Riprogramma_AfterInternalPause_SBStaysReady()
    {
        // Ops gestisce internamente: mette in pausa, riprogramma, SB mai cambiato
        var (svc, wo) = GiveReadyAndToSchedule();
        wo.Programma("Planner"); wo.AvviaEsecuzione("Team");
        wo.MettiInPausa("Veicolo ko", forCommercial: false);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status);

        wo.InterventoRisoltoRiprogramma("Riassegnato altro veicolo");

        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);
        Assert.Equal(ServiceBookedStatus.Ready, svc.Status); // sempre Ready
        Assert.False(wo.PausedForCommercial);
    }

    // ── Cascade: annullare un servizio dalla pausa annulla l'intera Quotation (review 2026-04-17)
    //    Policy: la Quotation e' un'entita' commerciale unitaria. Annullare un servizio equivale ad
    //    annullare il preventivo nel suo insieme (tutti i SB + WO della Quotation, aggiornamento Deal).
    //    Il legame MovingIds rende alcuni servizi interdipendenti; per coerenza il cascade e' sempre totale.

    [Fact]
    public void CascadeCancel_OnQuotation_TerminatesAllServices()
    {
        // Quotation con 2 SB (ritiro + consegna di un trasloco): annullare uno cascade su entrambi.
        var q = new Quotation { DealId = "d1" };
        var ritiro = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        var consegna = new ServiceBooked { Type = ServiceBookedType.Consegna };
        ritiro.MovingIds.Add(consegna.Id);
        consegna.MovingIds.Add(ritiro.Id);
        q.Services.Add(ritiro);
        q.Services.Add(consegna);
        q.Confirm();
        q.Finalize(); // InProgress -> Finalized
        ritiro.AccettaServizio(); ritiro.SegnaComePronto();
        consegna.AccettaServizio(); consegna.SegnaComePronto();

        var woR = new WorkOrder { Type = WorkOrderType.Commercial, ServiceBookedId = ritiro.Id };
        woR.ServizioPronto("c"); woR.NonProgrammabile("slot saturi");
        ritiro.RichiedeIntervento("Non programmabile");
        var woC = new WorkOrder { Type = WorkOrderType.Commercial, ServiceBookedId = consegna.Id };
        woC.ServizioPronto("c");

        // Cascade annullamento: tutti i SB + WO della Quotation
        foreach (var svc in q.Services) svc.Annulla();
        woR.Annulla("Commercial"); woC.Annulla("Commercial");
        q.Cancel();

        Assert.All(q.Services, s => Assert.Equal(ServiceBookedStatus.Cancelled, s.Status));
        Assert.Equal(WorkOrderStatus.Cancelled, woR.Status);
        Assert.Equal(WorkOrderStatus.Cancelled, woC.Status);
        Assert.Equal(QuotationStatus.Cancelled, q.Status);
    }

    [Fact]
    public void CascadeCancel_OnDraftQuotation_UsesArchiveInsteadOfCancel()
    {
        // Quotation in stato Draft/InProgress: non si puo' Cancel(), si usa Archive().
        var q = new Quotation { DealId = "d1" };
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        q.Services.Add(svc);
        q.Confirm(); // Draft -> InProgress (ancora non Finalized)

        foreach (var s in q.Services) s.Annulla();
        // Cancel() non si applica su InProgress -> uso Archive()
        q.Archive();

        Assert.Equal(QuotationStatus.Archived, q.Status);
        Assert.Equal(ServiceBookedStatus.Cancelled, svc.Status);
    }

    [Fact]
    public void OnQuotationCancelled_RemovesActivePlan_IfQuotationIdMatches()
    {
        var deal = new WeTacoo.Domain.Commercial.Deal { LeadId = "l1" };
        var q = new Quotation { DealId = deal.Id };
        var plan = new DraftPlan { Description = "Dep 5m3", MonthlyFee = 50m, EstimatedM3 = 5m };
        q.DraftPlans.Add(plan);
        deal.Quotations.Add(q);
        deal.CreatePlan(q, plan);
        Assert.NotNull(deal.ActivePlan);

        deal.OnQuotationCancelled(q.Id);

        Assert.Null(deal.ActivePlan);
    }

    [Fact]
    public void OnQuotationCancelled_Preserves_ActivePlan_IfDifferentQuotation()
    {
        // Deal con 2 Quotation: la prima ha generato l'ActivePlan, la seconda e' aggiuntiva.
        // Annullando la seconda, l'ActivePlan (legato alla prima) deve restare.
        var deal = new WeTacoo.Domain.Commercial.Deal { LeadId = "l1" };
        var q1 = new Quotation { DealId = deal.Id };
        var plan = new DraftPlan { Description = "Dep 5m3", MonthlyFee = 50m, EstimatedM3 = 5m };
        q1.DraftPlans.Add(plan);
        deal.Quotations.Add(q1);
        deal.CreatePlan(q1, plan);

        var q2 = new Quotation { DealId = deal.Id };
        deal.Quotations.Add(q2);

        deal.OnQuotationCancelled(q2.Id);

        Assert.NotNull(deal.ActivePlan);
        Assert.Equal(q1.Id, deal.ActivePlan.QuotationId);
    }

    [Fact]
    public void CascadeCancel_OnLastActiveQuotation_DiscardsDeal()
    {
        // Se tutte le Quotation del Deal diventano terminali negative, il Deal va in NotConverted
        // (Discard) se non era ancora stato convertito.
        var deal = new WeTacoo.Domain.Commercial.Deal { LeadId = "l1" };
        deal.Qualify(); deal.EnterNegotiation();
        var q = new Quotation { DealId = deal.Id };
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        q.Services.Add(svc);
        deal.Quotations.Add(q);
        q.Confirm();

        svc.Annulla();
        q.Archive();

        var allTerminal = deal.Quotations.All(x => x.Status is QuotationStatus.Archived or QuotationStatus.Cancelled);
        if (allTerminal) deal.Discard();

        Assert.Equal(WeTacoo.Domain.Commercial.Enums.DealStatus.NotConverted, deal.Status);
    }
}

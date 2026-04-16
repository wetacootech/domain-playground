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

namespace WeTacoo.Tests;

/// <summary>
/// Test end-to-end che percorre l'intera state machine di tutte le entità
/// in un singolo flusso lineare: UC-1 Deposito completo.
/// Ogni Assert verifica lo stato atteso dopo ogni azione.
/// </summary>
public class EndToEnd_StateMachineTests
{
    [Fact]
    public void UC1_Deposito_FullStateMachine()
    {
        // ══════════════════════════════════════════════════════
        // STEP 1: Lead + Deal
        // ══════════════════════════════════════════════════════
        var lead = new Lead { Personal = new Personal("Anna", "Verdi", "anna@test.com", "+39 333") };
        var deal = new Deal { LeadId = lead.Id, AreaId = "area-mi" };
        lead.AddDeal(deal.Id);

        Assert.Equal(DealStatus.ToQualify, deal.Status);

        // ══════════════════════════════════════════════════════
        // STEP 2: Qualifica → In trattativa
        // ══════════════════════════════════════════════════════
        deal.Qualify();
        Assert.Equal(DealStatus.Qualified, deal.Status);

        deal.EnterNegotiation();
        Assert.Equal(DealStatus.InNegotiation, deal.Status);

        // ══════════════════════════════════════════════════════
        // STEP 3: Quotation Bozza → In lavorazione → Finalizzato
        // ══════════════════════════════════════════════════════
        var quotation = new Quotation { DealId = deal.Id, IsInitial = true };
        quotation.Products.Add(new Product { Name = "Deposito Premium", Price = 129.90m });
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("Via Roma 1", "Milano", "20100", "area-mi"),
            ScheduledDate = DateTime.Today.AddDays(5),
            ScheduledSlot = "09:00-12:00"
        });
        quotation.DraftPlans.Add(new DraftPlan { MonthlyFee = 89.90m, EstimatedM3 = 10, AreaId = "area-mi" });
        deal.Quotations.Add(quotation);

        Assert.Equal(QuotationStatus.Draft, quotation.Status);

        quotation.Confirm();
        Assert.Equal(QuotationStatus.InProgress, quotation.Status);

        quotation.Finalize();
        Assert.Equal(QuotationStatus.Finalized, quotation.Status);
        Assert.True(quotation.IsAccepted);

        // ══════════════════════════════════════════════════════
        // STEP 4: Deal → Convertito + ActivePlan creato (Deal resta Convertito!)
        // ══════════════════════════════════════════════════════
        deal.Convert();
        Assert.Equal(DealStatus.Converted, deal.Status);

        deal.CreatePlan(quotation, quotation.DraftPlans[0]);
        Assert.NotNull(deal.ActivePlan);
        Assert.Equal(DealStatus.Converted, deal.Status); // NOT Active!

        // ══════════════════════════════════════════════════════
        // STEP 5: WorkOrder creato → InCompletamento
        // ══════════════════════════════════════════════════════
        var svc = quotation.Services[0];
        var wo = new WorkOrder
        {
            ServiceBookedId = svc.Id,
            ServiceAddress = svc.ServiceAddress?.Street,
            ContactName = "Anna Verdi",
            ScheduledDate = svc.ScheduledDate,
            ScheduledSlot = svc.ScheduledSlot,
        };
        svc.WorkOrderId = wo.Id;

        Assert.Equal(WorkOrderStatus.Completing, wo.Status);

        // ══════════════════════════════════════════════════════
        // STEP 6: Questionario completato → WO DaProgrammare
        // ══════════════════════════════════════════════════════
        var questionnaire = new Questionnaire { Origin = "Quotation" };
        questionnaire.IsVerified = true;

        wo.ServizioPronto("Commercial");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // ══════════════════════════════════════════════════════
        // STEP 7: Programma → WO Programmato
        // ══════════════════════════════════════════════════════
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);

        // ══════════════════════════════════════════════════════
        // STEP 8: Planning + Mission + Shift creati (WO resta Programmato)
        // ══════════════════════════════════════════════════════
        var woExec = new WorkOrder
        {
            Type = WorkOrderType.Commercial,
            ServiceBookedId = svc.Id,
            ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, false, "area-mi"),
            ServiceAddress = svc.ServiceAddress?.Street,
        };

        var planning = new Planning { Date = wo.ScheduledDate!.Value };
        var team = new PlanningTeam { OperatorIds = ["op-1", "op-2"] };
        planning.Teams.Add(team);
        var mission = planning.AddMission(team.Id, [new ServiceRef(woExec.Id, 100)], ["vehicle-1"]);

        var shift = new Shift
        {
            MissionId = mission.Id,
            Date = planning.Date,
            Mission = new MissionData(["op-1", "op-2"], ["vehicle-1"], [], "09:00-12:00"),
            Resources = new ShiftResources(["op-1", "op-2"], ["vehicle-1"], [])
        };
        shift.AddServiceEntry(woExec.Id, deal.Id, lead.Id, ServiceEntryType.Ritiro,
            new ClientData("Anna Verdi", "+39 333"));

        Assert.Equal("Created", shift.Status);
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status); // Still Programmato!

        // ══════════════════════════════════════════════════════
        // STEP 9: Avvia Shift → WO InEsecuzione
        // ══════════════════════════════════════════════════════
        shift.Start();
        Assert.Equal("InProgress", shift.Status);

        wo.AvviaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        // ══════════════════════════════════════════════════════
        // STEP 10: Esegui (censimento + carico)
        // ══════════════════════════════════════════════════════
        var entry = shift.ServiceEntries[0];
        // SE senza Start: l'in-progress vive sullo Shift InCorso
        Assert.False(entry.Completed);

        var obj1 = new PhysicalObject { Name = "Armadio", Volume = 2.0m, DealId = deal.Id, LeadId = lead.Id };
        obj1.PickUp(mission.Id);
        obj1.LoadOnVehicle(mission.Id);
        Assert.Equal(ObjectStatus.OnVehicle, obj1.Status);

        entry.Complete("Firma_Anna");
        Assert.True(entry.Completed);

        // ══════════════════════════════════════════════════════
        // STEP 11: Completa Shift → WO DaVerificare
        // ══════════════════════════════════════════════════════
        shift.Complete();
        Assert.Equal("Completed", shift.Status);

        wo.CompletaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        // Scarico oggetti a magazzino
        obj1.UnloadToWarehouse("wh-1", mission.Id);
        Assert.Equal(ObjectStatus.OnWarehouse, obj1.Status);

        // ══════════════════════════════════════════════════════
        // STEP 12: Verifica → WO Concluso → Quotation Completato → Deal Attivo
        // ══════════════════════════════════════════════════════
        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);

        // Quotation: Finalizzato → Completato (no differenze)
        quotation.Complete();
        Assert.Equal(QuotationStatus.Completed, quotation.Status);

        // Deal: Convertito → Attivo (recurring)
        deal.Activate();
        Assert.Equal(DealStatus.Active, deal.Status);

        // ══════════════════════════════════════════════════════
        // STEP 13: Chiusura — ActivePlan con 1 oggetto → resta Active
        // ══════════════════════════════════════════════════════
        var closed = deal.TryCloseIfNoObjectsRemaining(1);
        Assert.False(closed);
        Assert.Equal(DealStatus.Active, deal.Status);

        // 0 oggetti → Concluso
        closed = deal.TryCloseIfNoObjectsRemaining(0);
        Assert.True(closed);
        Assert.Equal(DealStatus.Concluded, deal.Status);

        // ══════════════════════════════════════════════════════
        // VERIFY: Payment
        // ══════════════════════════════════════════════════════
        var payment = new Payment { DealId = deal.Id, PaymentType = "Recurring" };
        payment.Products.Add(new SimplifiedProduct { Name = "Deposito Premium", Price = 129.90m });
        payment.AddCharge(129.90m);
        payment.ExecuteCharge(payment.Charges[0].Id);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
    }

    [Fact]
    public void UC2_Trasloco_FullStateMachine()
    {
        // ══════════════════════════════════════════════════════
        // SETUP: Deal OneOff con 2 servizi (Ritiro MI + Consegna TO)
        // ══════════════════════════════════════════════════════
        var lead = new Lead { Personal = new Personal("Laura", "Bianchi", "laura@test.com", "+39 340") };
        var deal = new Deal { LeadId = lead.Id, AreaId = "area-mi" };
        lead.AddDeal(deal.Id);

        deal.Qualify();
        deal.EnterNegotiation();

        var quotation = new Quotation { DealId = deal.Id, IsInitial = true };
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Ritiro,
            ServiceAddress = new Address("Via Torino 5", "Milano", "20100", "area-mi"),
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "09:00-12:00"
        });
        quotation.Services.Add(new ServiceBooked
        {
            Type = ServiceBookedType.Consegna,
            ServiceAddress = new Address("Corso Francia 10", "Torino", "10121", "area-to"),
            ScheduledDate = DateTime.Today.AddDays(7),
            ScheduledSlot = "14:00-17:00"
        });
        quotation.Products.Add(new Product { Name = "Trasloco", Price = 890m });
        deal.Quotations.Add(quotation);

        quotation.Confirm();
        quotation.Finalize();
        deal.Convert();

        Assert.Equal(DealStatus.Converted, deal.Status);
        Assert.Null(deal.ActivePlan); // OneOff

        // ══════════════════════════════════════════════════════
        // WorkOrder per ciascun servizio
        // ══════════════════════════════════════════════════════
        var woRitiro = new WorkOrder { ServiceAddress = "Via Torino 5", ScheduledDate = DateTime.Today.AddDays(7) };
        var woConsegna = new WorkOrder { ServiceAddress = "Corso Francia 10", ScheduledDate = DateTime.Today.AddDays(7) };
        quotation.Services[0].WorkOrderId = woRitiro.Id;
        quotation.Services[1].WorkOrderId = woConsegna.Id;

        Assert.Equal(WorkOrderStatus.Completing, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.Completing, woConsegna.Status);

        // ══════════════════════════════════════════════════════
        // Questionario → DaProgrammare → Programmato
        // ══════════════════════════════════════════════════════
        woRitiro.ServizioPronto("Commercial");
        woConsegna.ServizioPronto("Commercial");
        Assert.Equal(WorkOrderStatus.ToSchedule, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.ToSchedule, woConsegna.Status);

        woRitiro.Programma("Ops");
        woConsegna.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.Scheduled, woConsegna.Status);

        // ══════════════════════════════════════════════════════
        // Shift con 2 ServiceEntry
        // ══════════════════════════════════════════════════════
        var shift = new Shift { Date = DateTime.Today.AddDays(7) };
        var entryRitiro = shift.AddServiceEntry("wo-r", deal.Id, lead.Id,
            ServiceEntryType.TraslocoRitiro, new ClientData("Laura", "+39 340"));
        var entryConsegna = shift.AddServiceEntry("wo-c", deal.Id, lead.Id,
            ServiceEntryType.TraslocoConsegna, new ClientData("Laura", "+39 340"));

        // ══════════════════════════════════════════════════════
        // Avvia → InEsecuzione
        // ══════════════════════════════════════════════════════
        shift.Start();
        woRitiro.AvviaEsecuzione("Execution");
        woConsegna.AvviaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.InExecution, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.InExecution, woConsegna.Status);

        // ══════════════════════════════════════════════════════
        // Esegui: ritiro + consegna diretta (no magazzino)
        // ══════════════════════════════════════════════════════
        // SE senza Start
        var objects = Enumerable.Range(0, 5).Select(i =>
        {
            var o = new PhysicalObject { Name = $"Obj{i}", Volume = 0.5m, DealId = deal.Id };
            o.PickUp(); o.LoadOnVehicle();
            return o;
        }).ToList();
        entryRitiro.Complete("Firma_Ritiro");

        foreach (var o in objects) o.Deliver();
        entryConsegna.Complete("Firma_Consegna");

        Assert.NotEqual(entryRitiro.ClientInfo!.Signature, entryConsegna.ClientInfo!.Signature);
        Assert.All(objects, o => Assert.Equal(ObjectStatus.Delivered, o.Status));

        // ══════════════════════════════════════════════════════
        // Completa Shift → DaVerificare
        // ══════════════════════════════════════════════════════
        shift.Complete();
        woRitiro.CompletaEsecuzione("Execution");
        woConsegna.CompletaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.ToVerify, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.ToVerify, woConsegna.Status);

        // ══════════════════════════════════════════════════════
        // Verifica → Concluso → Quotation Completato → Deal Concluso (OneOff)
        // ══════════════════════════════════════════════════════
        woRitiro.VerificaEConcludi("Ops");
        woConsegna.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, woRitiro.Status);
        Assert.Equal(WorkOrderStatus.Concluded, woConsegna.Status);

        quotation.Complete();
        Assert.Equal(QuotationStatus.Completed, quotation.Status);

        // OneOff: Convertito → Concluso (salta Active)
        deal.Conclude();
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }

    [Fact]
    public void WorkOrder_BackTransitions()
    {
        var wo = new WorkOrder();
        Assert.Equal(WorkOrderStatus.Completing, wo.Status);

        // InCompletamento → DaProgrammare
        wo.ServizioPronto("Ops");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // DaProgrammare → InPausa (non programmabile)
        wo.NonProgrammabile("Ops");
        Assert.Equal(WorkOrderStatus.Paused, wo.Status);

        // InPausa → DaProgrammare (intervento risolto)
        wo.InterventoRisoltoRiprogramma("risolto");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // DaProgrammare → Programmato
        wo.Programma("Ops");
        Assert.Equal(WorkOrderStatus.Scheduled, wo.Status);

        // Programmato → DaProgrammare (riprogramma)
        wo.Riprogramma("Ops");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // Riprogramma → Programmato → InEsecuzione
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.InExecution, wo.Status);

        // InEsecuzione → DaProgrammare (operazione interrotta)
        wo.InterrompiEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.ToSchedule, wo.Status);

        // Riprova fino a Concluso
        wo.Programma("Ops");
        wo.AvviaEsecuzione("Execution");
        wo.CompletaEsecuzione("Execution");
        Assert.Equal(WorkOrderStatus.ToVerify, wo.Status);

        wo.VerificaEConcludi("Ops");
        Assert.Equal(WorkOrderStatus.Concluded, wo.Status);
    }

    [Fact]
    public void Quotation_MismatchFlow()
    {
        var quotation = new Quotation { DealId = "deal-1" };
        quotation.Confirm();
        quotation.Finalize();
        Assert.Equal(QuotationStatus.Finalized, quotation.Status);

        // Servizio completato con differenze → DaAdeguare
        quotation.MarkToAdjust();
        Assert.Equal(QuotationStatus.ToAdjust, quotation.Status);

        // Segna come completato
        quotation.Complete();
        Assert.Equal(QuotationStatus.Completed, quotation.Status);
    }

    [Fact]
    public void Shift_FullStateMachine()
    {
        var shift = new Shift { Date = DateTime.Today };

        Assert.Equal("Created", shift.Status);

        shift.Start(); // → InCorso
        Assert.Equal("InProgress", shift.Status);

        shift.Pause(); // → InPausa
        Assert.Equal("Paused", shift.Status);

        shift.Resume(); // → InCorso
        Assert.Equal("InProgress", shift.Status);

        shift.Suspend(); // → Sospesa (parziale)
        Assert.Equal("Suspended", shift.Status);

        shift.Restart(); // → InCorso
        Assert.Equal("InProgress", shift.Status);

        shift.Complete(); // → Completata
        Assert.Equal("Completed", shift.Status);
    }

    [Fact]
    public void Deal_InvalidTransitions_Comprehensive()
    {
        var deal = new Deal { };

        // Can't skip states
        deal.Activate();
        Assert.Equal(DealStatus.ToQualify, deal.Status);

        deal.Conclude();
        Assert.Equal(DealStatus.ToQualify, deal.Status);

        // Normal flow
        deal.Qualify();
        deal.Convert(); // Can't convert from Qualified — needs InNegotiation first
        Assert.Equal(DealStatus.Qualified, deal.Status);

        deal.EnterNegotiation();
        Assert.Equal(DealStatus.InNegotiation, deal.Status);

        deal.Convert(); // InNegotiation → Converted
        Assert.Equal(DealStatus.Converted, deal.Status);

        // Can't discard after conversion
        deal.Discard();
        Assert.Equal(DealStatus.Converted, deal.Status);

        // Activate (recurring)
        deal.Activate();
        Assert.Equal(DealStatus.Active, deal.Status);

        // Can conclude from Active
        deal.Conclude();
        Assert.Equal(DealStatus.Concluded, deal.Status);
    }
}

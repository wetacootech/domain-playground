using WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;
using WeTacoo.Domain.Commercial.ValueObjects;
using WeTacoo.Domain.SharedInfrastructure;
using WeTacoo.Domain.Operational;
using WeTacoo.Domain.Operational.Entities;
using WeTacoo.Domain.Operational.Enums;
using WeTacoo.Domain.Operational.ValueObjects;
using WeTacoo.Domain.Execution;
using WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Execution.Enums;
using WeTacoo.Domain.Financial;
using WeTacoo.Domain.Financial.Enums;
using WeTacoo.Domain.Identity;
using WeTacoo.Domain.Marketing;
using WeTacoo.Domain.Happiness;
using WeTacoo.Domain.Events;

namespace WeTacoo.Playground.Web.Services;

/// <summary>
/// Step di uno scenario UC. BranchPointNote (quando non null) segnala che questo step e' l'ultimo deterministico dell'UC:
/// gli step successivi illustrano UN possibile outcome scelto dal catalogo UC. Per testare altri outcome, fermarsi qui
/// e proseguire manualmente dal Workspace.
/// </summary>
public record ScenarioStep(string Name, string Description, string BC, Func<Task> Execute, string? BranchPointNote = null);

/// <summary>
/// ScenarioEngine — 13 scenari 1:1 con gli UC-1..UC-13 di UseCases-Solutions.md.
/// Ogni scenario riflette le transizioni di stato e gli eventi dei test UC*_*.cs (ground truth).
/// </summary>
public class ScenarioEngine(PlaygroundState state)
{
    private readonly PlaygroundState _state = state;

    private void Emit(IDomainEvent evt) => _state.PublishEvent(evt);

    // ══════════════════════════════════════════════════════
    // UC-1: Ritiro e deposito standard (Recurring)
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC1_DepositoStandardSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        Questionnaire? questionnaire = null;
        ServiceBooked? svc = null;
        WorkOrder? wo = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        MarketingClient? mktClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> pickedObjects = new();

        return
        [
            new("Step 1: Lead e Deal Recurring", "Usa Lead Anna Verdi dal seed, crea Deal Recurring (area Milano). Deal nasce ToQualify.", "Commercial", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = _state.Leads.First(l => l.Personal.LastName == "Verdi");
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);

                mktClient = _state.MarketingClients.FirstOrDefault(m => m.CommercialLeadId == lead.Id);
                mktClient?.AddDeal(deal.Id);

                Emit(new DealCreatedEvent(deal.Id, lead.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Qualifica e negoziazione Deal", "Deal.Qualify() -> Qualified, EnterNegotiation() -> InNegotiation.", "Commercial", async () =>
            {
                deal!.Qualify();
                Emit(new DealQualifiedEvent(deal.Id));
                deal.EnterNegotiation();
                mktClient?.AdvanceFunnel("Qualified");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Quotation Deposito Premium", "Crea Quotation (Deposito Premium 129.90, DraftPlan 10m3, ServiceBooked Ritiro).", "Commercial", async () =>
            {
                quotation = new Quotation
                {
                    DealId = deal!.Id,
                    IsInitial = true,
                    Services = [new ServiceBooked
                    {
                        Type = ServiceBookedType.Ritiro,
                        ServiceAddress = new Address("20121", _state.Areas[0].Id),
                        ScheduledDate = DateTime.Today.AddDays(5),
                        ScheduledSlot = "09:00-12:00"
                    }],
                    Products = [new Product { Name = "Deposito Premium", Price = 129.90m }],
                    DraftPlans = [new DraftPlan { Description = "Piano deposito 10m\u00b3", MonthlyFee = 89.90m, EstimatedM3 = 10, AreaId = _state.Areas[0].Id }],
                    PaymentCondition = new PaymentCondition(22m, 30)
                };
                deal.Quotations.Add(quotation);
                svc = quotation.Services[0];
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Cliente accetta, Deal converte, crea ActivePlan", "Quotation.Finalize(), Deal.Convert() (stays Converted), CreatePlan dal DraftPlan. ServiceBooked DaCompletare. FinancialClient + Payment.", "Commercial", async () =>
            {
                quotation!.Finalize();
                deal!.Convert();
                lead!.MarkConverted();
                Emit(new QuotationAcceptedEvent(quotation.Id, deal.Id, lead.Id, true));
                Emit(new DealConvertedEvent(deal.Id, lead.Id));
                Emit(new LeadConvertedEvent(lead.Id));

                deal.CreatePlan(quotation, quotation.DraftPlans[0]);

                svc!.AccettaServizio();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Anna Verdi" };
                _state.FinancialClients.Add(finClient);
                lead.FinancialClientId = finClient.Id;

                payment = new Payment { ClientId = finClient.Id, DealId = deal.Id, QuotationId = quotation.Id, PaymentType = "Recurring", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Deposito Premium", Price = 129.90m });
                _state.Payments.Add(payment);
                Emit(new PaymentCreatedEvent(payment.Id, deal.Id));

                mktClient?.AdvanceFunnel("Converted");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Questionario e WorkOrder", "Crea Questionnaire (IsVerified=true), ServiceBooked SegnaComePronto -> Pronto. WorkOrder (InCompletamento) -> ServizioPronto -> DaProgrammare.", "Commercial", async () =>
            {
                questionnaire = new Questionnaire { Origin = "Quotation", IsVerified = true };
                questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Piano dell'abitazione?", "number", "3", "client") });
                questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Ascensore?", "boolean", "true", "client") });
                questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Volume stimato m3?", "number", "10", "client") });
                _state.Questionnaires.Add(questionnaire);
                quotation!.QuestionnaireId = questionnaire.Id; // Questionnaire unico per Quotation (review 2026-04-17)
                svc!.SegnaComePronto();

                wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svc.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    Commercial = new CommercialData(lead!.Id, questionnaire.Id, null, [], false),
                    ServiceAddress = svc.ServiceAddress?.ZipCode,
                    ContactName = "Anna Verdi",
                    EstimatedVolume = 10,
                    ScheduledDate = svc.ScheduledDate,
                    ScheduledSlot = svc.ScheduledSlot
                };
                _state.WorkOrders.Add(wo);
                svc.WorkOrderId = wo.Id;
                Emit(new WorkOrderCreatedEvent(wo.Id, wo.Type.ToString(), "Ritiro"));
                wo.ServizioPronto("Commercial");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "Completing", "ToSchedule", "Servizio pronto"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Planning e Mission", "Crea Planning, Team, Resources e Mission (ServiceRef 100% al WO). WorkOrder Programma -> Programmato.", "Operational", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(5) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(team);
                var vehicleRes = new Resource { ResourceType = "vehicle", SourceId = _state.Vehicles[0].Id, AreaId = _state.Areas[0].Id };
                planning.Resources.Add(vehicleRes);
                planning.AddMission(team.Id, [new ServiceRef(wo!.Id, 100)], [vehicleRes.Id]);
                _state.Plannings.Add(planning);

                wo.Programma("RespOps");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "ToSchedule", "Scheduled", "Scheduled"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Shift creato e avviato", "Crea Shift dalla Mission con ServiceEntry Ritiro. Shift.Start() e WorkOrder AvviaEsecuzione -> InEsecuzione.", "Execution", async () =>
            {
                var mission = planning!.Missions[0];
                shift = new Shift
                {
                    MissionId = mission.Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-12:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shift.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Anna Verdi", "+39 333 1234567"));
                _state.Shifts.Add(shift);
                Emit(new ShiftCreatedEvent(shift.Id, mission.Id));

                wo.AvviaEsecuzione("Execution");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "Scheduled", "InExecution", "Avvio esecuzione"));

                shift.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                Emit(new OperationStartedEvent(shift.Id, wo.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Censimento e carico oggetti", "5 PhysicalObject creati, PickUp + LoadOnVehicle. Task Censimento. ServiceEntry.Complete(firma).", "Execution", async () =>
            {
                var mission = planning!.Missions[0];
                var entry = shift!.ServiceEntries[0];
                var names = new[] { "Armadio 2 ante", "Scrivania", "Scatola libri", "Materasso", "Divano 3 posti" };
                var volumes = new[] { 1.8m, 0.8m, 0.3m, 0.5m, 2.0m };
                for (int i = 0; i < names.Length; i++)
                {
                    var obj = new PhysicalObject { Name = names[i], Volume = volumes[i], LeadId = lead!.Id, DealId = deal!.Id };
                    obj.PickUp(mission.Id);
                    obj.LoadOnVehicle(mission.Id);
                    _state.Objects.Add(obj);
                    pickedObjects.Add(obj);
                    Emit(new ObjectStateChangedEvent(obj.Id, "Draft", "OnVehicle", "Ritiro"));
                }
                shift.AddTask(TaskType.Censimento, entry.Id, pickedObjects.Select(o => o.Id).ToList());
                entry.Complete("Anna Verdi [firma]");
                Emit(new ServiceExecutedEvent(shift.Id, entry.Id, wo!.Id, "Completed"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Scarico a magazzino e chiusura Shift", "WarehouseOperation IN. Oggetti UnloadToWarehouse. Shift.Complete().", "Execution", async () =>
            {
                var whOp = new WarehouseOperation
                {
                    WarehouseId = warehouse!.Id,
                    MissionId = planning!.Missions[0].Id,
                    VehicleId = _state.Vehicles[0].Id,
                    OperationType = "IN",
                    ObjectIds = pickedObjects.Select(o => o.Id).ToList()
                };
                whOp.Start();
                foreach (var obj in pickedObjects)
                {
                    obj.UnloadToWarehouse(warehouse.Id, planning.Missions[0].Id);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "OnWarehouse", "Scarico"));
                }
                whOp.Complete();
                _state.WarehouseOperations.Add(whOp);
                shift!.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: WorkOrder conclusione", "OperationCompletedEvent -> WO.CompletaEsecuzione -> DaVerificare. WO.VerificaEConcludi -> Concluso.", "Operational", async () =>
            {
                wo!.ActualVolume = pickedObjects.Sum(o => o.Volume);
                Emit(new OperationCompletedEvent(shift!.Id, wo.Id));
                wo.VerificaEConcludi("RespOps");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "ToVerify", "Concluded", "Verificato e concluso"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 11: ServizioCompletato -> Deal Active", "ServizioCompletatoEvent (ActualVolume, HasDifferences=false). ServiceBooked -> Completato. Quotation Complete. Deal.Activate() via cascade.", "Commercial", async () =>
            {
                Emit(new ServizioCompletatoEvent(wo!.Id, svc!.Id, wo.ActualVolume, false, "5 oggetti ritirati"));
                lead!.RecalculateStatus([deal!.Status]);
                Emit(new LeadStatusChangedEvent(lead.Id, lead.Status.ToString()));
                mktClient?.AdvanceFunnel("Active");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 12: Primo addebito mensile", "Payment.AddCharge(89.90), Charge.Execute(). Happiness registra soddisfazione 5.", "Financial", async () =>
            {
                var charge = payment!.AddCharge(89.90m);
                charge.Execute();
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, 89.90m));

                var hClient = new HappinessClient { CommercialLeadId = lead!.Id };
                hClient.RecordSatisfaction(wo!.Id, 5, "Servizio puntuale e professionale");
                _state.HappinessClients.Add(hClient);
                Emit(new SatisfactionRecordedEvent(hClient.Id, wo.Id, 5));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-2: Trasloco punto-punto (OneOff, ritiro MI + consegna TO)
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC2_TraslocoSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svcRitiro = null;
        ServiceBooked? svcConsegna = null;
        WorkOrder? woRitiro = null;
        WorkOrder? woConsegna = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        List<PhysicalObject> objects = new();

        return
        [
            new("Step 1: Lead e Deal OneOff (trasloco MI->TO)", "Crea Lead Laura Bianchi e Deal OneOff area Milano.", "Commercial", async () =>
            {
                lead = new Lead { Personal = new Personal("Laura", "Bianchi", "laura.bianchi@test.com", "+39 340 0000001") };
                _state.Leads.Add(lead);
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                Emit(new DealCreatedEvent(deal.Id, lead.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Qualifica e Quotation trasloco", "Qualify + EnterNegotiation. Quotation con 2 ServiceBooked (Ritiro MI + Consegna TO). NESSUN DraftPlan (OneOff).", "Commercial", async () =>
            {
                deal!.Qualify();
                deal.EnterNegotiation();

                quotation = new Quotation { DealId = deal.Id, IsInitial = true };
                svcRitiro = new ServiceBooked
                {
                    Type = ServiceBookedType.Ritiro,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    ScheduledDate = DateTime.Today.AddDays(7),
                    ScheduledSlot = "09:00-12:00"
                };
                svcConsegna = new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = new Address("10121", _state.Areas[2].Id),
                    ScheduledDate = DateTime.Today.AddDays(7),
                    ScheduledSlot = "14:00-17:00"
                };
                quotation.Services.Add(svcRitiro);
                quotation.Services.Add(svcConsegna);
                // Trasloco: MovingIds appaia Ritiro <-> Consegna (DDD5 §2.2e, review 2026-04-16)
                svcRitiro.MovingIds.Add(svcConsegna.Id);
                svcConsegna.MovingIds.Add(svcRitiro.Id);
                quotation.Products.Add(new Product { Name = "Trasloco bilocale", Price = 890m });
                quotation.Products.Add(new Product { Name = "Supplemento MI->TO", Price = 150m });
                deal.Quotations.Add(quotation);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Accetta Quotation e converti Deal", "Quotation.Finalize, Deal.Convert. Nessun ActivePlan (OneOff). ServiceBooked AccettaServizio.", "Commercial", async () =>
            {
                quotation!.Finalize();
                deal!.Convert();
                lead!.MarkConverted();
                Emit(new QuotationAcceptedEvent(quotation.Id, deal.Id, lead.Id, false));
                Emit(new DealConvertedEvent(deal.Id, lead.Id));

                svcRitiro!.AccettaServizio();
                svcConsegna!.AccettaServizio();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Laura Bianchi" };
                _state.FinancialClients.Add(finClient);
                lead.FinancialClientId = finClient.Id;

                payment = new Payment { ClientId = finClient.Id, DealId = deal.Id, QuotationId = quotation.Id, PaymentType = "OneOff", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Trasloco bilocale", Price = 890m });
                payment.Products.Add(new SimplifiedProduct { Name = "Supplemento MI->TO", Price = 150m });
                _state.Payments.Add(payment);
                Emit(new PaymentCreatedEvent(payment.Id, deal.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Due WorkOrder (Ritiro + Consegna) appaiati via MovingIds", "Crea WO Ritiro (area-mi) e WO Consegna (area-to). CommercialData.MovingIds traccia l'accoppiamento trasloco. Entrambi ServizioPronto dopo SegnaComePronto su ServiceBooked.", "Operational", async () =>
            {
                svcRitiro!.SegnaComePronto();
                svcConsegna!.SegnaComePronto();

                woRitiro = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcRitiro.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    Commercial = new CommercialData(lead!.Id, null, null, [.. svcRitiro.MovingIds], false),
                    ServiceAddress = "Via Torino 5, Milano",
                    ContactName = "Laura Bianchi",
                    ScheduledDate = svcRitiro.ScheduledDate,
                    ScheduledSlot = svcRitiro.ScheduledSlot
                };
                woConsegna = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcConsegna.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[2].Id),
                    Commercial = new CommercialData(lead.Id, null, null, [.. svcConsegna.MovingIds], false),
                    ServiceAddress = "Corso Francia 10, Torino",
                    ContactName = "Laura Bianchi",
                    ScheduledDate = svcConsegna.ScheduledDate,
                    ScheduledSlot = svcConsegna.ScheduledSlot
                };
                _state.WorkOrders.Add(woRitiro);
                _state.WorkOrders.Add(woConsegna);
                svcRitiro.WorkOrderId = woRitiro.Id;
                svcConsegna.WorkOrderId = woConsegna.Id;
                Emit(new WorkOrderCreatedEvent(woRitiro.Id, "Commercial", "Ritiro"));
                Emit(new WorkOrderCreatedEvent(woConsegna.Id, "Commercial", "Consegna"));

                woRitiro.ServizioPronto("Commercial");
                woConsegna.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Mission unica con due ServiceRef", "Mission ha 2 ServiceRef (wo-ritiro 100% + wo-consegna 100%). WO entrambi Programma -> Programmato.", "Operational", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(7) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(team);
                planning.AddMission(team.Id, [new ServiceRef(woRitiro!.Id, 100), new ServiceRef(woConsegna!.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning);

                woRitiro.Programma("RespOps");
                woConsegna.Programma("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Shift con TraslocoRitiro + TraslocoConsegna", "Shift con 2 ServiceEntry (TraslocoRitiro + TraslocoConsegna). Start. WO entrambi AvviaEsecuzione.", "Execution", async () =>
            {
                var mission = planning!.Missions[0];
                shift = new Shift
                {
                    MissionId = mission.Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-17:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                shift.AddServiceEntry(woRitiro!.Id, deal!.Id, lead!.Id, ServiceEntryType.TraslocoRitiro, new ClientData("Laura Bianchi", "+39 340 0000001"));
                shift.AddServiceEntry(woConsegna!.Id, deal.Id, lead.Id, ServiceEntryType.TraslocoConsegna, new ClientData("Laura Bianchi", "+39 340 0000001"));
                _state.Shifts.Add(shift);
                Emit(new ShiftCreatedEvent(shift.Id, mission.Id));

                woRitiro.AvviaEsecuzione("Execution");
                woConsegna.AvviaEsecuzione("Execution");
                shift.Start();
                Emit(new OperationStartedEvent(shift.Id, woRitiro.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Ritiro MI - PickUp e LoadOnVehicle", "EntryRitiro.Start. 5 oggetti PickUp+LoadOnVehicle. Task Censimento + Carico. entryRitiro.Complete(firma ritiro).", "Execution", async () =>
            {
                var entry = shift!.ServiceEntries[0];
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                for (int i = 0; i < 5; i++)
                {
                    var obj = new PhysicalObject { Name = $"Oggetto trasloco {i}", Volume = 0.5m, DealId = deal!.Id, LeadId = lead!.Id };
                    obj.PickUp(shift.MissionId);
                    obj.LoadOnVehicle(shift.MissionId);
                    _state.Objects.Add(obj);
                    objects.Add(obj);
                }
                shift.AddTask(TaskType.Censimento, entry.Id, objects.Select(o => o.Id).ToList());
                shift.AddTask(TaskType.Carico, entry.Id, objects.Select(o => o.Id).ToList());
                entry.Complete("Laura Bianchi [firma ritiro]");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Movimento MI->TO", "Task Movimento trasversale (ServiceEntryId=null).", "Execution", async () =>
            {
                shift!.AddTask(TaskType.Movimento, null);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Consegna TO - Deliver diretto (no magazzino)", "EntryConsegna.Start. Oggetti Deliver() direttamente (OnVehicle->Delivered). Task Scarico. Entry.Complete(firma consegna). Shift.Complete.", "Execution", async () =>
            {
                var entryConsegna = shift!.ServiceEntries[1];
                // entryConsegna.Start() rimosso (DDD5 2026-04-14)
                foreach (var obj in objects)
                {
                    obj.Deliver(shift.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "Delivered", "Consegna"));
                }
                shift.AddTask(TaskType.Scarico, entryConsegna.Id, objects.Select(o => o.Id).ToList());
                entryConsegna.Complete("Laura Bianchi [firma consegna]");
                shift.Complete();
                Emit(new OperationCompletedEvent(shift.Id, woRitiro!.Id));
                Emit(new OperationCompletedEvent(shift.Id, woConsegna!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: WorkOrder conclusione e ServiziCompletati", "WO entrambi VerificaEConcludi. Emetti ServizioCompletatoEvent per entrambi -> ServiceBooked Completato -> Quotation Complete -> Deal.Activate (cascade) -> Deal.Conclude.", "Operational", async () =>
            {
                woRitiro!.VerificaEConcludi("RespOps");
                woConsegna!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woRitiro.Id, svcRitiro!.Id, 2.5m, false, "Ritiro completato"));
                Emit(new ServizioCompletatoEvent(woConsegna.Id, svcConsegna!.Id, 2.5m, false, "Consegna completata"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 11: Payment OneOff eseguito", "Payment.AddCharge(1040), ExecuteCharge -> Payment Paid.", "Financial", async () =>
            {
                var charge = payment!.AddCharge(1040m);
                payment.ExecuteCharge(charge.Id);
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, 1040m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 12: Deal Concluded e Happiness", "Deal chiude (0 oggetti rimanenti OneOff + allServicesCompleted). Happiness soddisfazione registrata.", "Commercial", async () =>
            {
                deal!.TryCloseIfNoObjectsRemaining(0, allServicesCompleted: true);
                if (deal.Status == DealStatus.Concluded)
                    Emit(new DealConcludedEvent(deal.Id, lead!.Id));

                lead!.RecalculateStatus([deal.Status]);
                Emit(new LeadStatusChangedEvent(lead.Id, lead.Status.ToString()));

                var hClient = new HappinessClient { CommercialLeadId = lead.Id };
                hClient.RecordSatisfaction(woRitiro!.Id, 5, "Trasloco fluido");
                _state.HappinessClients.Add(hClient);
                Emit(new SatisfactionRecordedEvent(hClient.Id, woRitiro.Id, 5));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-3: Consegna parziale da deposito attivo
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC3_ConsegnaParzialeSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? qDeposito = null;
        Quotation? qConsegna = null;
        ServiceBooked? svcConsegna = null;
        WorkOrder? woConsegna = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> objectsInWH = new();
        List<PhysicalObject> objectsToDeliver = new();

        return
        [
            new("Step 1: Setup - Deal Active con 30 oggetti in deposito", "Deal Recurring Active (30 oggetti OnWarehouse), ActivePlan 129/30m3. Simula stato post-UC1.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = _state.Leads.First(l => l.Personal.LastName == "Romano");
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                lead.MarkConverted();

                deal.Qualify();
                deal.EnterNegotiation();
                deal.Convert();

                qDeposito = new Quotation { DealId = deal.Id, IsInitial = true, Status = QuotationStatus.Completed };
                qDeposito.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m, AreaId = _state.Areas[0].Id });
                deal.Quotations.Add(qDeposito);
                deal.CreatePlan(qDeposito, qDeposito.DraftPlans[0]);
                deal.Activate();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Giulia Romano" };
                _state.FinancialClients.Add(finClient);
                lead.FinancialClientId = finClient.Id;

                for (int i = 0; i < 30; i++)
                {
                    var obj = new PhysicalObject { Name = $"Scatola {i}", Volume = 1m, DealId = deal.Id, LeadId = lead.Id };
                    obj.StockDirectly(warehouse.Id);
                    _state.Objects.Add(obj);
                    objectsInWH.Add(obj);
                }
                deal.TryCloseIfNoObjectsRemaining(30);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Nuova Quotation di Consegna (sullo stesso Deal)", "Crea quotation IsInitial=false con ServiceBooked Consegna e SelectedObjectIds (2 specifici). Empty DraftPlans.", "Commercial", async () =>
            {
                qConsegna = new Quotation { DealId = deal!.Id, IsInitial = false };
                var selected = objectsInWH.Take(2).ToList();
                objectsToDeliver = selected;
                svcConsegna = new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = new Address("20122", _state.Areas[0].Id),
                    SelectedObjectIds = selected.Select(o => o.Id).ToList(),
                    ScheduledDate = DateTime.Today.AddDays(3),
                    ScheduledSlot = "09:00-12:00"
                };
                qConsegna.Services.Add(svcConsegna);
                qConsegna.Products.Add(new Product { Name = "Consegna parziale 2 oggetti", Price = 120m });
                deal!.Quotations.Add(qConsegna);

                foreach (var obj in selected) obj.ReservedByQuotationId = qConsegna.Id;
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Finalize e accetta Consegna", "qConsegna.Finalize. svcConsegna.AccettaServizio -> DaCompletare -> SegnaComePronto.", "Commercial", async () =>
            {
                qConsegna!.Finalize();
                Emit(new QuotationAcceptedEvent(qConsegna.Id, deal!.Id, lead!.Id, false));
                svcConsegna!.AccettaServizio();
                svcConsegna.SegnaComePronto();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: WorkOrder Consegna", "WO Commercial tipo Consegna (IsPartial=true). ServizioPronto -> DaProgrammare.", "Operational", async () =>
            {
                woConsegna = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcConsegna!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, true, false, _state.Areas[0].Id),
                    Commercial = new CommercialData(lead!.Id, null, null, [], false),
                    ServiceAddress = svcConsegna.ServiceAddress?.ZipCode,
                    ContactName = "Giulia Romano",
                    EstimatedVolume = 2m,
                    ScheduledDate = svcConsegna.ScheduledDate,
                    ScheduledSlot = svcConsegna.ScheduledSlot
                };
                _state.WorkOrders.Add(woConsegna);
                svcConsegna.WorkOrderId = woConsegna.Id;
                Emit(new WorkOrderCreatedEvent(woConsegna.Id, "Commercial", "Consegna"));
                woConsegna.ServizioPronto("Commercial");

                payment = new Payment { ClientId = finClient!.Id, DealId = deal!.Id, QuotationId = qConsegna!.Id, PaymentType = "OneOff", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Consegna parziale", Price = 120m });
                _state.Payments.Add(payment);
                Emit(new PaymentCreatedEvent(payment.Id, deal.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Planning + Mission + WarehouseOperation OUT", "Planning, Mission consegna. WarehouseOperation OUT preparatoria (Start+Complete).", "Operational", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(3) };
                var team = new PlanningTeam { OperatorIds = [_state.Operators[0].Id] };
                planning.Teams.Add(team);
                var mission = planning.AddMission(team.Id, [new ServiceRef(woConsegna!.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning);

                var whOut = new WarehouseOperation
                {
                    WarehouseId = warehouse!.Id,
                    MissionId = mission.Id,
                    OperationType = "OUT",
                    ObjectIds = objectsToDeliver.Select(o => o.Id).ToList()
                };
                whOut.Start();
                whOut.Complete();
                _state.WarehouseOperations.Add(whOut);

                woConsegna.Programma("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Shift Consegna - LoadFromWarehouse + Deliver", "Shift con ServiceEntry Consegna. WO AvviaEsecuzione. Oggetti LoadFromWarehouse -> OnVehicle -> Deliver.", "Execution", async () =>
            {
                var mission = planning!.Missions[0];
                shift = new Shift
                {
                    MissionId = mission.Id,
                    Date = planning.Date,
                    Mission = new MissionData([_state.Operators[0].FullName], [_state.Vehicles[0].Name], [], "09:00-12:00"),
                    Resources = new ShiftResources([_state.Operators[0].Id], [_state.Vehicles[0].Id], [])
                };
                var entry = shift.AddServiceEntry(woConsegna!.Id, deal!.Id, lead!.Id, ServiceEntryType.Consegna, new ClientData("Giulia Romano", "+39 328 5551234"));
                _state.Shifts.Add(shift);
                Emit(new ShiftCreatedEvent(shift.Id, mission.Id));

                woConsegna.AvviaEsecuzione("Execution");
                shift.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                Emit(new OperationStartedEvent(shift.Id, woConsegna.Id));

                foreach (var obj in objectsToDeliver)
                {
                    obj.LoadFromWarehouse(mission.Id);
                    obj.Deliver(mission.Id);
                    obj.ReservedByQuotationId = null;
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnWarehouse", "Delivered", "Consegna parziale"));
                }
                entry.Complete("Giulia Romano [firma]");
                shift.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Chiusura WorkOrder Consegna", "OperationCompletedEvent -> WO CompletaEsecuzione -> DaVerificare -> Concluso.", "Operational", async () =>
            {
                Emit(new OperationCompletedEvent(shift!.Id, woConsegna!.Id));
                woConsegna.VerificaEConcludi("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: ServizioCompletato e Plan aggiornato", "ServizioCompletatoEvent -> svcConsegna Completato, qConsegna Complete. ActivePlan.UpdateAfterPartialDelivery(2, 2m). 28 oggetti restanti -> Deal resta Active.", "Commercial", async () =>
            {
                Emit(new ServizioCompletatoEvent(woConsegna!.Id, svcConsegna!.Id, 2m, false, "2 oggetti consegnati"));
                deal!.ActivePlan!.UpdateAfterPartialDelivery(2, 2m);
                var remaining = _state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse);
                deal.TryCloseIfNoObjectsRemaining(remaining);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Addebito consegna parziale", "Payment.AddCharge(120), ExecuteCharge -> Paid.", "Financial", async () =>
            {
                var charge = payment!.AddCharge(120m);
                payment.ExecuteCharge(charge.Id);
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, 120m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-4: Ritiro che dura 3 giorni (multi-Mission)
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC4_RitiroMultiGiornoSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svc = null;
        WorkOrder? wo = null;
        Planning? planningD1 = null;
        Planning? planningD2 = null;
        Shift? shiftD1 = null;
        Shift? shiftD2 = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> objectsD1 = new();
        List<PhysicalObject> objectsD2 = new();

        return
        [
            new("Step 1: Lead ufficio + Deal OneOff grande", "Crea Lead Rag. Bianchi (ufficio), Deal OneOff, Qualify+EnterNegotiation.", "Commercial", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Rag.", "Bianchi", "ufficio@bianchi.it", "+39 02 0000000") };
                _state.Leads.Add(lead);
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                Emit(new DealCreatedEvent(deal.Id, lead.Id));
                deal.Qualify();
                deal.EnterNegotiation();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Quotation unica, 1 ServiceBooked Ritiro (grande)", "Un solo ServiceBooked Ritiro (200 oggetti, 3 giorni stimati).", "Commercial", async () =>
            {
                quotation = new Quotation { DealId = deal!.Id, IsInitial = true };
                svc = new ServiceBooked
                {
                    Type = ServiceBookedType.Ritiro,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    ScheduledDate = DateTime.Today.AddDays(10),
                    ScheduledSlot = "09:00-17:00",
                    Notes = "Ufficio 3 piani, ~200 oggetti"
                };
                quotation.Services.Add(svc);
                quotation.Products.Add(new Product { Name = "Ritiro ufficio 3 giorni", Price = 3500m });
                deal!.Quotations.Add(quotation);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Accetta Quotation", "Finalize, Deal.Convert, ServiceBooked AccettaServizio + SegnaComePronto.", "Commercial", async () =>
            {
                quotation!.Finalize();
                deal!.Convert();
                lead!.MarkConverted();
                Emit(new QuotationAcceptedEvent(quotation.Id, deal.Id, lead.Id, false));
                Emit(new DealConvertedEvent(deal.Id, lead.Id));

                svc!.AccettaServizio();
                svc.SegnaComePronto();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Bianchi srl" };
                _state.FinancialClients.Add(finClient);

                payment = new Payment { ClientId = finClient.Id, DealId = deal.Id, QuotationId = quotation.Id, PaymentType = "OneOff", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Ritiro ufficio 3 giorni", Price = 3500m });
                _state.Payments.Add(payment);
                Emit(new PaymentCreatedEvent(payment.Id, deal.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: WorkOrder unico, pronto a pianificare", "Un solo WO Ritiro. ServizioPronto -> DaProgrammare. Multi-Mission previste.", "Operational", async () =>
            {
                wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svc!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    ServiceAddress = svc.ServiceAddress?.ZipCode,
                    ContactName = "Rag. Bianchi",
                    EstimatedVolume = 100m,
                    ScheduledDate = svc.ScheduledDate,
                    ScheduledSlot = svc.ScheduledSlot
                };
                _state.WorkOrders.Add(wo);
                svc.WorkOrderId = wo.Id;
                Emit(new WorkOrderCreatedEvent(wo.Id, "Commercial", "Ritiro"));
                wo.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Giorno 1 - Planning + Mission con volume 50%", "Planning giorno 1, Mission con ServiceRef 50%. WO.Programma -> Programmato.", "Operational", async () =>
            {
                planningD1 = new Planning { Date = DateTime.Today.AddDays(10) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planningD1.Teams.Add(team);
                planningD1.AddMission(team.Id, [new ServiceRef(wo!.Id, 50)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planningD1);
                wo.Programma("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Giorno 1 - Shift Sospeso, solo 50/75 oggetti", "Shift avviato, SE resta Completed=false (outcome parziale nel payload ServiceEntryOutcome), Problem notato. WO AvviaEsecuzione. Shift.Suspend.", "Execution", async () =>
            {
                var mission = planningD1!.Missions[0];
                shiftD1 = new Shift
                {
                    MissionId = mission.Id,
                    Date = planningD1.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-17:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shiftD1.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Rag. Bianchi", "+39 02 0000000"));
                _state.Shifts.Add(shiftD1);
                Emit(new ShiftCreatedEvent(shiftD1.Id, mission.Id));

                wo.AvviaEsecuzione("Execution");
                shiftD1.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                Emit(new OperationStartedEvent(shiftD1.Id, wo.Id));

                for (int i = 0; i < 50; i++)
                {
                    var obj = new PhysicalObject { Name = $"Piano0-{i}", Volume = 0.5m, DealId = deal.Id, LeadId = lead.Id };
                    obj.PickUp(mission.Id);
                    obj.LoadOnVehicle(mission.Id);
                    obj.UnloadToWarehouse(warehouse!.Id, mission.Id);
                    _state.Objects.Add(obj);
                    objectsD1.Add(obj);
                }
                shiftD1.AddTask(TaskType.Censimento, entry.Id, objectsD1.Select(o => o.Id).ToList());
                // DDD5 2026-04-14: outcome parziale tracciato nel payload chiusura Shift (ServiceEntryOutcome), SE resta Completed=false, Shift chiude Sospesa
                var _outcomeD1 = new ServiceEntryOutcome(entry.Id, "parziale", "50/75 piano terra", ResidualVolume: 25 * 0.5m);
                shiftD1.Problems.Add("50/75 piano terra - serve continuazione domani");
                shiftD1.Suspend();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Giorno 2 - Nuova Planning+Mission (stesso WO)", "Planning giorno 2 con Mission continuation (stesso WO, 50%). WO resta InEsecuzione (multi-Mission).", "Operational", async () =>
            {
                planningD2 = new Planning { Date = DateTime.Today.AddDays(11) };
                var team = new PlanningTeam { OperatorIds = new List<string> { _state.Operators[2].Id, _state.Operators[0].Id } };
                planningD2.Teams.Add(team);
                planningD2.AddMission(team.Id, [new ServiceRef(wo!.Id, 50)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planningD2);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Giorno 2 - Shift Completa, team cambia (sostituto)", "Shift giorno 2 registra operatori presenti diversi da quelli pianificati. Entry.Complete firma. Oggetti restanti al magazzino.", "Execution", async () =>
            {
                var mission = planningD2!.Missions[0];
                shiftD2 = new Shift
                {
                    MissionId = mission.Id,
                    Date = planningD2.Date,
                    Mission = new MissionData(new List<string> { _state.Operators[2].FullName, _state.Operators[0].FullName }, [_state.Vehicles[0].Name], [], "09:00-17:00"),
                    Resources = new ShiftResources(new List<string> { _state.Operators[2].Id, _state.Operators[1].Id }, [_state.Vehicles[0].Id], [])
                };
                var entry = shiftD2.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Rag. Bianchi", "+39 02 0000000"));
                _state.Shifts.Add(shiftD2);
                Emit(new ShiftCreatedEvent(shiftD2.Id, mission.Id));

                shiftD2.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                for (int i = 0; i < 60; i++)
                {
                    var obj = new PhysicalObject { Name = $"Piano12-{i}", Volume = 0.5m, DealId = deal.Id, LeadId = lead.Id };
                    obj.PickUp(mission.Id);
                    obj.LoadOnVehicle(mission.Id);
                    obj.UnloadToWarehouse(warehouse!.Id, mission.Id);
                    _state.Objects.Add(obj);
                    objectsD2.Add(obj);
                }
                shiftD2.AddTask(TaskType.Censimento, entry.Id, objectsD2.Select(o => o.Id).ToList());
                entry.Complete("Rag. Bianchi [firma conclusione]");
                shiftD2.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Chiusura WorkOrder - tutti gli Shift Completati", "OperationCompletedEvent per entrambi gli Shift -> WO CompletaEsecuzione (tutti Shift Completata) -> DaVerificare -> Concluso.", "Operational", async () =>
            {
                Emit(new OperationCompletedEvent(shiftD1!.Id, wo!.Id));
                Emit(new OperationCompletedEvent(shiftD2!.Id, wo.Id));
                wo.VerificaEConcludi("RespOps");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "ToVerify", "Concluded", "Verificato e concluso"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Conclusione Deal (OneOff senza deposito)", "ServizioCompletatoEvent -> svc Completato, quotation Complete. Deal.Activate cascade, poi Conclude (OneOff no plan, allServicesCompleted).", "Commercial", async () =>
            {
                Emit(new ServizioCompletatoEvent(wo!.Id, svc!.Id, (objectsD1.Count + objectsD2.Count) * 0.5m, false, "Ritiro ufficio completato in 2 giorni"));
                deal!.TryCloseIfNoObjectsRemaining(_state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse), allServicesCompleted: true);
                lead!.RecalculateStatus([deal.Status]);
                Emit(new LeadStatusChangedEvent(lead.Id, lead.Status.ToString()));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 11: Payment OneOff Paid", "Payment.AddCharge(3500), ExecuteCharge -> Paid.", "Financial", async () =>
            {
                var charge = payment!.AddCharge(3500m);
                payment.ExecuteCharge(charge.Id);
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, 3500m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-5: Sopralluogo che cambia il preventivo (DDD5 §4.8 review 2026-04-16: Sopralluogo e' un WorkOrder)
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC5_SopralluogoSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svcRitiro = null;
        WorkOrder? woSopralluogo = null;
        Questionnaire? questionnaire = null;

        return
        [
            new("Step 1: Lead e Deal dubbio", "Crea Lead Marco Neri, Deal OneOff area Milano. Qualify+EnterNegotiation.", "Commercial", async () =>
            {
                lead = _state.Leads.First(l => l.Personal.LastName == "Neri");
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                Emit(new DealCreatedEvent(deal.Id, lead.Id));

                deal.Qualify();
                deal.EnterNegotiation();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Quotation iniziale (solo Ritiro)", "Quotation con 1 ServiceBooked Ritiro (volume dichiarato 18m3). Confirm -> InProgress (condizione per richiedere sopralluogo).", "Commercial", async () =>
            {
                quotation = new Quotation { DealId = deal!.Id, IsInitial = true };
                svcRitiro = new ServiceBooked
                {
                    Type = ServiceBookedType.Ritiro,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id)
                };
                quotation.Services.Add(svcRitiro);
                quotation.Products.Add(new Product { Name = "Ritiro (18 m3 dichiarati)", Price = 400m });
                deal.Quotations.Add(quotation);
                quotation.Confirm(); // Draft -> InProgress: ora e' possibile richiedere sopralluogo
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Sales Richiedi sopralluogo sul Ritiro", "Crea Questionnaire template + WorkOrder Sopralluogo (Type=Sopralluogo). svcRitiro.RichiediSopralluogo -> WaitingInspection.", "Commercial", async () =>
            {
                questionnaire = new Questionnaire { Origin = "Sopralluogo" };
                questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Volume totale?", "number", null, "all") });
                questionnaire.Questions.Add(new Question { Data = new QuestionAnswer("Piani aggiuntivi?", "boolean", null, "all") });
                _state.Questionnaires.Add(questionnaire);

                woSopralluogo = new WorkOrder
                {
                    Type = WorkOrderType.Sopralluogo,
                    ServiceBookedId = svcRitiro!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Sopralluogo, false, false, _state.Areas[0].Id),
                    Commercial = new CommercialData(lead!.Id, questionnaire.Id, "Sopralluogo propedeutico al Ritiro", [], false),
                    ServiceAddress = svcRitiro.ServiceAddress?.ZipCode,
                    ContactName = "Marco Neri",
                    ScheduledDate = DateTime.Today.AddDays(2)
                };
                _state.WorkOrders.Add(woSopralluogo);
                Emit(new WorkOrderCreatedEvent(woSopralluogo.Id, "Sopralluogo", "Sopralluogo"));

                quotation!.QuestionnaireId = questionnaire.Id; // Questionnaire unico per Quotation
                svcRitiro.RichiediSopralluogo(woSopralluogo.Id);
                Emit(new SopralluogoRichiestoEvent(svcRitiro.Id, woSopralluogo.Id, questionnaire.Id, svcRitiro.ServiceAddress?.ZipCode, "Volume dichiarato sospetto — verificare in sito"));

                woSopralluogo.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Shift sopralluogo creato + avviato + questionario compilato + chiuso (auto-conclude)", "Planner crea Shift per il WO Sopralluogo, operatore compila questionnaire e chiude lo Shift. L'handler OperationCompletedEvent su WO tipo Sopralluogo salta ToVerify e auto-conclude + SopralluogoCompletato sul ServiceBooked.", "Execution", async () =>
            {
                woSopralluogo!.Programma("RespOps");
                var shiftSopr = new Shift
                {
                    MissionId = null,
                    Date = DateTime.Today.AddDays(2)
                };
                shiftSopr.ServiceEntries.Add(new ServiceEntry
                {
                    ServiceId = woSopralluogo.Id,
                    LeadId = lead!.Id,
                    Type = ServiceEntryType.Sopralluogo,
                    Inspection = new InspectionData(woSopralluogo.Id, questionnaire!.Id),
                    ClientInfo = new ClientData("Marco Neri", "+39 333 0000")
                });
                _state.Shifts.Add(shiftSopr);

                shiftSopr.Start();
                Emit(new OperationStartedEvent(shiftSopr.Id, woSopralluogo.Id));

                questionnaire.AnswerQuestion(questionnaire.Questions[0].Id, "28");
                questionnaire.AnswerQuestion(questionnaire.Questions[1].Id, "true");

                shiftSopr.ServiceEntries[0].Complete();
                shiftSopr.Complete();
                Emit(new OperationCompletedEvent(shiftSopr.Id, woSopralluogo.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Sales rinegozia prezzo Ritiro", "Sales legge risultati questionario (28 m3 reali) e aggiorna Products: Ritiro 650m. Quotation resta InProgress.", "Commercial", async () =>
            {
                quotation!.Products.Clear();
                quotation.Products.Add(new Product { Name = "Ritiro (28 m3, post-sopralluogo)", Price = 650m });
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Cliente accetta nuovo prezzo", "quotation.Finalize, Deal.Convert, svcRitiro.AccettaServizio. Con sopralluogo concluso + QuestionnaireReady salta ToComplete e va direttamente a Ready.", "Commercial", async () =>
            {
                quotation!.Finalize();
                deal!.Convert();
                lead!.MarkConverted();
                Emit(new QuotationAcceptedEvent(quotation.Id, deal.Id, lead.Id, false));
                Emit(new DealConvertedEvent(deal.Id, lead.Id));
                svcRitiro!.AccettaServizio();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: WorkOrder Ritiro (post-sopralluogo)", "ServiceBooked gia' Ready dal sopralluogo: crea WorkOrder Commercial Ritiro con volume reale 28m3. ServizioPronto -> ToSchedule.", "Operational", async () =>
            {
                var wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcRitiro.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    Commercial = new CommercialData(lead!.Id, questionnaire!.Id, "Volume da sopralluogo: 28 m3", [], false),
                    ServiceAddress = svcRitiro.ServiceAddress?.ZipCode,
                    ContactName = "Marco Neri",
                    EstimatedVolume = 28m
                };
                _state.WorkOrders.Add(wo);
                svcRitiro.WorkOrderId = wo.Id;
                Emit(new WorkOrderCreatedEvent(wo.Id, "Commercial", "Ritiro"));
                wo.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-6: Problema in esecuzione che rimbalza al commerciale
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC6_ProblemaEsecuzioneSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svc = null;
        WorkOrder? wo = null;
        Planning? planning1 = null;
        Planning? planning2 = null;
        Shift? shift1 = null;
        Shift? shift2 = null;
        Warehouse? warehouse = null;

        return
        [
            new("Step 1: Setup esecuzione in corso", "Lead+Deal+Quotation+WO completi fino a InEsecuzione con Shift avviato (scenario pre-problema).", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Famiglia", "Rossi", "rossi@test.com", "+39 333 0000") };
                _state.Leads.Add(lead);
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                deal.Qualify();
                deal.EnterNegotiation();
                deal.Convert();
                lead.MarkConverted();

                quotation = new Quotation { DealId = deal.Id, IsInitial = true };
                svc = new ServiceBooked
                {
                    Type = ServiceBookedType.Ritiro,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id)
                };
                quotation.Services.Add(svc);
                quotation.Products.Add(new Product { Name = "Ritiro 75 oggetti", Price = 700m });
                deal.Quotations.Add(quotation);
                quotation.Finalize();
                svc.AccettaServizio();
                svc.SegnaComePronto();

                wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svc.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    EstimatedVolume = 15m
                };
                _state.WorkOrders.Add(wo);
                svc.WorkOrderId = wo.Id;
                wo.ServizioPronto("Commercial");
                wo.Programma("RespOps");
                wo.AvviaEsecuzione("Execution");
                Emit(new WorkOrderCreatedEvent(wo.Id, "Commercial", "Ritiro"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Shift campo registra problema", "Team avvia Shift, entry.Start, ritirano piani superiori, poi scoprono seminterrato allagato. shift.Problems.Add.", "Execution", async () =>
            {
                planning1 = new Planning { Date = DateTime.Today.AddDays(2) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning1.Teams.Add(team);
                planning1.AddMission(team.Id, [new ServiceRef(wo!.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning1);

                shift1 = new Shift
                {
                    MissionId = planning1.Missions[0].Id,
                    Date = planning1.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shift1.AddServiceEntry(wo.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Fam. Rossi", "+39 333 0000"));
                _state.Shifts.Add(shift1);
                shift1.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                shift1.Problems.Add("Seminterrato allagato - serve pompa e giorno extra");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Shift termina Sospeso, registra oggetti ritirati", "Team ritira solo piani superiori (75 oggetti). SE resta Completed=false (outcome parziale). shift.Suspend. OperationInterruptedEvent.", "Execution", async () =>
            {
                var entry = shift1!.ServiceEntries[0];
                for (int i = 0; i < 75; i++)
                {
                    var obj = new PhysicalObject { Name = $"Piano-{i}", Volume = 0.2m, DealId = deal!.Id, LeadId = lead!.Id };
                    obj.PickUp(shift1.MissionId);
                    obj.LoadOnVehicle(shift1.MissionId);
                    obj.UnloadToWarehouse(warehouse!.Id, shift1.MissionId);
                    _state.Objects.Add(obj);
                }
                // DDD5 2026-04-14: outcome parziale nel payload chiusura Shift; SE resta Completed=false; Shift chiude Sospesa
                var _outcomePartial = new ServiceEntryOutcome(entry.Id, "parziale", "Seminterrato allagato - solo piani superiori");
                shift1.Suspend();
                Emit(new OperationInterruptedEvent(shift1.Id, wo!.Id, "Seminterrato allagato"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Operational qualifica e mette WO In pausa", "wo.MettiInPausa(motivo) -> InPausa. Emette RichiedeInterventoEvent a Commercial.", "Operational", async () =>
            {
                wo!.MettiInPausa("Seminterrato allagato - serve decisione commerciale");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "InExecution", "Paused", "Seminterrato allagato"));
                Emit(new RichiedeInterventoEvent(wo.Id, svc!.Id, "Seminterrato allagato - serve pompa + giorno extra"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: ServiceBooked riceve intervento", "RichiedeInterventoEvent -> svc.RichiedeIntervento -> DaCompletare. Quotation MarkToVerify.", "Commercial", async () =>
            {
                quotation!.MarkToVerify();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }, BranchPointNote: "UC-6 biforca qui: Sales sceglie tra 3 outcome InterventoRisolto: (a) Riprogramma [cliente accetta extra, flusso implementato sotto], (b) Riprendi (cliente accetta senza ulteriore pausa operativa), (c) Chiudi (cliente rifiuta extra, chiusura a volume ridotto)."),

            new("Step 6: Sales contatta cliente - cliente accetta extra", "Sales propone +200 per giorno extra e pompa. Cliente accetta.", "Commercial", async () =>
            {
                quotation!.Verify(); // ToVerify -> Finalized
                quotation.Products.Add(new Product { Name = "Giorno extra + pompa seminterrato", Price = 200m });
                svc!.InterventoRisolto();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: WO InterventoRisoltoRiprogramma", "wo.InterventoRisoltoRiprogramma(note) -> InPausa -> DaProgrammare. Emit InterventoRisoltoEvent.", "Commercial", async () =>
            {
                wo!.InterventoRisoltoRiprogramma("Cliente accetta - nuovo giorno+pompa");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "Paused", "ToSchedule", "Intervento risolto - riprogramma"));
                Emit(new InterventoRisoltoEvent(wo.Id, "riprogramma", "nuovo giorno+pompa"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Planning giorno extra - secondo Shift", "Planning nuova data con asset pompa. Mission stesso WO. Programma -> AvviaEsecuzione.", "Operational", async () =>
            {
                planning2 = new Planning { Date = DateTime.Today.AddDays(4) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning2.Teams.Add(team);
                planning2.AddMission(team.Id, [new ServiceRef(wo!.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning2);

                wo.Programma("RespOps");
                wo.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Shift giorno extra - ritiro seminterrato", "Shift avviato con asset pompa. Entry.Start. 20 oggetti seminterrato ritirati. Complete+firma.", "Execution", async () =>
            {
                shift2 = new Shift
                {
                    MissionId = planning2!.Missions[0].Id,
                    Date = planning2.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], ["pompa"], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], ["pompa"])
                };
                var entry = shift2.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Fam. Rossi", "+39 333 0000"));
                _state.Shifts.Add(shift2);
                shift2.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                for (int i = 0; i < 20; i++)
                {
                    var obj = new PhysicalObject { Name = $"Seminterrato-{i}", Volume = 0.5m, DealId = deal.Id, LeadId = lead.Id };
                    obj.PickUp(shift2.MissionId);
                    obj.LoadOnVehicle(shift2.MissionId);
                    obj.UnloadToWarehouse(warehouse!.Id, shift2.MissionId);
                    _state.Objects.Add(obj);
                }
                entry.Complete("Fam. Rossi [firma conclusione]");
                shift2.Complete();
                Emit(new OperationCompletedEvent(shift2.Id, wo.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Chiusura WO e Deal", "wo.VerificaEConcludi. ServizioCompletatoEvent cascade -> svc Completato, quotation Complete, Deal Conclude.", "Operational", async () =>
            {
                wo!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(wo.Id, svc!.Id, 19.5m, true, "Ritiro completato in 2 giorni, extra pompa"));
                deal!.TryCloseIfNoObjectsRemaining(_state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse), allServicesCompleted: true);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-7: Servizio extra non preventivato
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC7_ServizioExtraSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svc = null;
        WorkOrder? wo = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;

        return
        [
            new("Step 1: Setup - Deal, Quotation, WO pronto", "Lead+Deal+Quotation con smontaggio armadio (preventivato). WO fino a InEsecuzione.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Chiara", "Longo", "longo@test.com", "+39 333 0101") };
                _state.Leads.Add(lead);
                Emit(new LeadCreatedEvent(lead.Id));

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                deal.Qualify();
                deal.EnterNegotiation();
                deal.Convert();
                lead.MarkConverted();

                quotation = new Quotation { DealId = deal.Id, IsInitial = true };
                svc = new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("20100", _state.Areas[0].Id) };
                quotation.Services.Add(svc);
                quotation.Products.Add(new Product { Name = "Ritiro + smontaggio armadio", Price = 350m });
                deal.Quotations.Add(quotation);
                quotation.Finalize();
                svc.AccettaServizio();
                svc.SegnaComePronto();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Chiara Longo" };
                _state.FinancialClients.Add(finClient);

                payment = new Payment { ClientId = finClient.Id, DealId = deal.Id, QuotationId = quotation.Id, PaymentType = "OneOff", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Ritiro + smontaggio armadio", Price = 350m });
                _state.Payments.Add(payment);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: WorkOrder e pianificazione", "Crea WO Ritiro. ServizioPronto -> DaProgrammare. Planning + Mission. Programma -> AvviaEsecuzione.", "Operational", async () =>
            {
                wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svc!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    EstimatedVolume = 5m
                };
                _state.WorkOrders.Add(wo);
                svc.WorkOrderId = wo.Id;
                Emit(new WorkOrderCreatedEvent(wo.Id, "Commercial", "Ritiro"));
                wo.ServizioPronto("Commercial");

                planning = new Planning { Date = DateTime.Today.AddDays(3) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(team);
                planning.AddMission(team.Id, [new ServiceRef(wo.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning);

                wo.Programma("RespOps");
                wo.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Shift start e task preventivato (smontaggio armadio)", "Shift avviato, entry.Start. Task Smontaggio IsExtra=false.", "Execution", async () =>
            {
                shift = new Shift
                {
                    MissionId = planning!.Missions[0].Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shift.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Chiara Longo", "+39 333 0101"));
                _state.Shifts.Add(shift);
                shift.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                Emit(new OperationStartedEvent(shift.Id, wo.Id));

                var planned = shift.AddTask(TaskType.Smontaggio, entry.Id);
                planned.Notes = "Smontaggio armadio (preventivato)";
                planned.EndTime = DateTime.UtcNow;
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Sorpresa - letto a castello richiede smontaggio extra", "Cliente chiede smontaggio letto a castello (non preventivato). Team aggiunge Task IsExtra=true.", "Execution", async () =>
            {
                var entry = shift!.ServiceEntries[0];
                var extra = shift.AddTask(TaskType.Smontaggio, entry.Id);
                extra.IsExtra = true;
                extra.Notes = "Smontaggio letto a castello (extra non preventivato)";
                extra.StartTime = DateTime.UtcNow.AddMinutes(-45);
                extra.EndTime = DateTime.UtcNow;
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Ritiro oggetti e chiusura Shift", "15 oggetti PickUp+LoadOnVehicle+UnloadToWarehouse. entry.Complete firma. Shift.Complete.", "Execution", async () =>
            {
                var entry = shift!.ServiceEntries[0];
                for (int i = 0; i < 15; i++)
                {
                    var obj = new PhysicalObject { Name = $"Oggetto {i}", Volume = 0.4m, DealId = deal!.Id, LeadId = lead!.Id };
                    obj.PickUp(shift.MissionId);
                    obj.LoadOnVehicle(shift.MissionId);
                    obj.UnloadToWarehouse(warehouse!.Id, shift.MissionId);
                    _state.Objects.Add(obj);
                }
                entry.Complete("Chiara Longo [firma]");
                shift.Complete();
                Emit(new OperationCompletedEvent(shift.Id, wo!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: WO completa ciclo normalmente", "WO.VerificaEConcludi -> Concluso. Gli extra non bloccano la verifica.", "Operational", async () =>
            {
                wo!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(wo.Id, svc!.Id, 6m, true, "Extra: smontaggio letto a castello 45 min"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Quotation MarkToAdjust (post-esecuzione)", "Quotation passa a ToAdjust (non ToVerify, perche' post-esecuzione).", "Commercial", async () =>
            {
                if (quotation!.Status == QuotationStatus.ToAdjust)
                {
                    // already set by cascade
                }
                else if (quotation.Status == QuotationStatus.Finalized)
                {
                    quotation.MarkToAdjust();
                }
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }, BranchPointNote: "UC-7 biforca qui: Sales decide (a) addebitare extra al cliente [flusso sotto: SimplifiedProduct + charge], (b) assorbire il costo dell'extra (quotation.Complete senza aggiungere prodotti)."),

            new("Step 8: Sales decide - addebito extra", "Sales aggiunge SimplifiedProduct 'Smontaggio extra (letto)' 80m. Quotation.Complete.", "Commercial", async () =>
            {
                payment!.Products.Add(new SimplifiedProduct { Name = "Smontaggio extra (letto)", Price = 80m });
                if (quotation!.Status == QuotationStatus.ToAdjust) quotation.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Financial esegue addebito totale", "Payment.AddCharge(430), ExecuteCharge -> Paid.", "Financial", async () =>
            {
                var charge = payment!.AddCharge(payment.TotalAmount);
                payment.ExecuteCharge(charge.Id);
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, payment.TotalAmount));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Deal Concluded e Happiness", "TryCloseIfNoObjectsRemaining. Happiness 4 stelle.", "Commercial", async () =>
            {
                deal!.TryCloseIfNoObjectsRemaining(_state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse), allServicesCompleted: true);

                var hClient = new HappinessClient { CommercialLeadId = lead!.Id };
                hClient.RecordSatisfaction(wo!.Id, 4, "Servizio rapido, extra gestito professionalmente");
                _state.HappinessClients.Add(hClient);
                Emit(new SatisfactionRecordedEvent(hClient.Id, wo.Id, 4));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-8: Oggetti rifiutati alla consegna
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC8_OggettiRifiutatiSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? qConsegna = null;
        ServiceBooked? svcConsegna = null;
        WorkOrder? woConsegna = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? refund = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> delivered = new();
        PhysicalObject? rejected = null;

        return
        [
            new("Step 1: Setup - Deal Active con 5 oggetti OnWarehouse", "Deal Recurring Active, 5 oggetti in deposito pronti per consegna.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Mario", "Rossi", "mario.rossi@test.com", "+39 333 0202") };
                _state.Leads.Add(lead);

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                lead.MarkConverted();
                deal.Qualify();
                deal.EnterNegotiation();
                deal.Convert();
                var qInit = new Quotation { DealId = deal.Id, IsInitial = true, Status = QuotationStatus.Completed };
                qInit.DraftPlans.Add(new DraftPlan { MonthlyFee = 99m, EstimatedM3 = 10m });
                deal.Quotations.Add(qInit);
                deal.CreatePlan(qInit, qInit.DraftPlans[0]);
                deal.Activate();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Mario Rossi" };
                _state.FinancialClients.Add(finClient);

                for (int i = 0; i < 5; i++)
                {
                    var obj = new PhysicalObject { Name = i == 4 ? "Tavolo pregiato" : $"Scatola {i}", Volume = 1m, DealId = deal.Id, LeadId = lead.Id };
                    obj.StockDirectly(warehouse.Id);
                    _state.Objects.Add(obj);
                    if (i < 4) delivered.Add(obj);
                    else rejected = obj;
                }
                deal.TryCloseIfNoObjectsRemaining(5);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Nuova Quotation Consegna totale", "Crea qConsegna con SelectedObjectIds (5 oggetti), Product 'Consegna 5 oggetti'.", "Commercial", async () =>
            {
                qConsegna = new Quotation { DealId = deal!.Id, IsInitial = false };
                svcConsegna = new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    SelectedObjectIds = _state.Objects.Where(o => o.DealId == deal.Id).Select(o => o.Id).ToList(),
                    ScheduledDate = DateTime.Today.AddDays(4)
                };
                qConsegna.Services.Add(svcConsegna);
                qConsegna.Products.Add(new Product { Name = "Consegna 5 oggetti", Price = 200m });
                deal.Quotations.Add(qConsegna);

                qConsegna.Finalize();
                svcConsegna.AccettaServizio();
                svcConsegna.SegnaComePronto();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: WorkOrder Consegna e pianificazione", "Crea WO, ServizioPronto, Programma, AvviaEsecuzione.", "Operational", async () =>
            {
                woConsegna = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcConsegna!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[0].Id)
                };
                _state.WorkOrders.Add(woConsegna);
                svcConsegna.WorkOrderId = woConsegna.Id;
                Emit(new WorkOrderCreatedEvent(woConsegna.Id, "Commercial", "Consegna"));
                woConsegna.ServizioPronto("Commercial");

                planning = new Planning { Date = DateTime.Today.AddDays(4) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(team);
                planning.AddMission(team.Id, [new ServiceRef(woConsegna.Id, 100)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning);

                woConsegna.Programma("RespOps");
                woConsegna.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: WarehouseOp OUT e carico su veicolo", "Tutti i 5 oggetti LoadFromWarehouse -> OnVehicle. WarehouseOperation OUT Start+Complete.", "Execution", async () =>
            {
                var mission = planning!.Missions[0];
                var whOut = new WarehouseOperation
                {
                    WarehouseId = warehouse!.Id,
                    MissionId = mission.Id,
                    OperationType = "OUT",
                    ObjectIds = _state.Objects.Where(o => o.DealId == deal!.Id).Select(o => o.Id).ToList()
                };
                whOut.Start();
                foreach (var obj in _state.Objects.Where(o => o.DealId == deal!.Id).ToList())
                    obj.LoadFromWarehouse(mission.Id);
                whOut.Complete();
                _state.WarehouseOperations.Add(whOut);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Consegna - 4 OK, 1 rifiutato (graffiato)", "Shift start. Entry.Start. 4 oggetti Deliver, il tavolo resta OnVehicle con IsReported=true.", "Execution", async () =>
            {
                shift = new Shift
                {
                    MissionId = planning!.Missions[0].Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shift.AddServiceEntry(woConsegna!.Id, deal!.Id, lead!.Id, ServiceEntryType.Consegna, new ClientData("Mario Rossi", "+39 333 0202"));
                _state.Shifts.Add(shift);
                shift.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                Emit(new OperationStartedEvent(shift.Id, woConsegna.Id));

                foreach (var obj in delivered)
                {
                    obj.Deliver(shift.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "Delivered", "Consegna"));
                }

                rejected!.IsReported = true;
                rejected.ReportedReason = "Cliente ha rifiutato: graffio sul tavolo";

                entry.Complete("Mario Rossi [firma - 4/5 accettati]");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Rientro oggetto rifiutato in magazzino", "rejected.UnloadToWarehouse -> OnVehicle -> OnWarehouse. WarehouseOperation IN dedicata.", "Execution", async () =>
            {
                rejected!.UnloadToWarehouse(warehouse!.Id, shift!.MissionId);
                Emit(new ObjectStateChangedEvent(rejected.Id, "OnVehicle", "OnWarehouse", "Rientro per rifiuto"));

                var whIn = new WarehouseOperation
                {
                    WarehouseId = warehouse.Id,
                    MissionId = shift.MissionId,
                    OperationType = "IN",
                    ObjectIds = [rejected.Id]
                };
                whIn.Start();
                whIn.Complete();
                _state.WarehouseOperations.Add(whIn);

                shift.Complete();
                Emit(new OperationCompletedEvent(shift.Id, woConsegna!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: WorkOrder Concluso, ServiceBooked Completato", "WO VerificaEConcludi. ServizioCompletatoEvent con HasDifferences=true -> Quotation ToAdjust.", "Operational", async () =>
            {
                woConsegna!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woConsegna.Id, svcConsegna!.Id, 4m, true, "1 oggetto rifiutato - rientro magazzino"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Sales decide rimborso trasporto", "Sales crea Payment 'Rimborso trasporto tavolo' 15m. Quotation Complete.", "Commercial", async () =>
            {
                refund = new Payment { ClientId = finClient!.Id, DealId = deal!.Id, PaymentType = "OneOff", VatRate = 22 };
                refund.Products.Add(new SimplifiedProduct { Name = "Rimborso trasporto tavolo", Price = 15m });
                _state.Payments.Add(refund);
                Emit(new PaymentCreatedEvent(refund.Id, deal.Id));

                if (qConsegna!.Status == QuotationStatus.ToAdjust) qConsegna.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Financial esegue rimborso", "refund.AddCharge(15) con note 'Rimborso trasporto oggetto danneggiato'. Execute.", "Financial", async () =>
            {
                var charge = refund!.AddCharge(15m);
                charge.Notes = "Rimborso trasporto oggetto danneggiato";
                charge.Execute();
                Emit(new ChargeExecutedEvent(refund.Id, charge.Id, 15m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Plan aggiornato - 4 consegnati, 1 ancora in deposito", "ActivePlan.UpdateAfterPartialDelivery(4, 4m). Deal resta Active (1 oggetto ancora in WH).", "Commercial", async () =>
            {
                deal!.ActivePlan!.UpdateAfterPartialDelivery(4, 4m);
                var remaining = _state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse);
                deal.TryCloseIfNoObjectsRemaining(remaining);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-9: Stesso cliente, due depositi in due citta' diverse
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC9_DueDepositiSteps()
    {
        Lead? lead = null;
        Deal? dealMI = null;
        Deal? dealRM = null;
        Quotation? qMI = null;
        Quotation? qRM = null;
        Quotation? qConsMI = null;
        Quotation? qConsRM = null;
        Payment? payMI = null;
        Payment? payRM = null;
        FinancialClient? finClient = null;
        Warehouse? whMI = null;
        Warehouse? whRM = null;
        List<PhysicalObject> objsMI = new();
        List<PhysicalObject> objsRM = new();

        return
        [
            new("Step 1: Un Lead, due Deal in aree diverse", "Lead Giulia Neri. Deal-MI Recurring (area Milano) + Deal-RM Recurring (area Roma). Lead.AddDeal per entrambi.", "Commercial", async () =>
            {
                whMI = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                whRM = _state.Warehouses.First(w => w.Name.Contains("Roma"));

                lead = new Lead { Personal = new Personal("Giulia", "Neri", "giulia.neri@test.com", "+39 333 0303") };
                _state.Leads.Add(lead);
                Emit(new LeadCreatedEvent(lead.Id));

                dealMI = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                dealRM = new Deal { LeadId = lead.Id, AreaId = _state.Areas[1].Id };
                _state.Deals.Add(dealMI);
                _state.Deals.Add(dealRM);
                lead.AddDeal(dealMI.Id);
                lead.AddDeal(dealRM.Id);
                Emit(new DealCreatedEvent(dealMI.Id, lead.Id));
                Emit(new DealCreatedEvent(dealRM.Id, lead.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Quotation + Qualify + Convert per entrambi", "qMI (99m, 20m3) e qRM (79m, 15m3). Qualify+EnterNegotiation+Convert per ciascun Deal.", "Commercial", async () =>
            {
                foreach (var d in new[] { dealMI, dealRM })
                {
                    d!.Qualify();
                    d.EnterNegotiation();
                    d.Convert();
                }
                lead!.MarkConverted();

                qMI = new Quotation { DealId = dealMI!.Id, IsInitial = true };
                qMI.Services.Add(new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("20100", _state.Areas[0].Id) });
                qMI.DraftPlans.Add(new DraftPlan { MonthlyFee = 99m, EstimatedM3 = 20m, AreaId = _state.Areas[0].Id });
                qMI.Products.Add(new Product { Name = "Deposito MI 20m3", Price = 99m });
                dealMI.Quotations.Add(qMI);

                qRM = new Quotation { DealId = dealRM!.Id, IsInitial = true };
                qRM.Services.Add(new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("00100", _state.Areas[1].Id) });
                qRM.DraftPlans.Add(new DraftPlan { MonthlyFee = 79m, EstimatedM3 = 15m, AreaId = _state.Areas[1].Id });
                qRM.Products.Add(new Product { Name = "Deposito RM 15m3", Price = 79m });
                dealRM.Quotations.Add(qRM);

                qMI.Finalize();
                qRM.Finalize();
                Emit(new QuotationAcceptedEvent(qMI.Id, dealMI.Id, lead.Id, true));
                Emit(new QuotationAcceptedEvent(qRM.Id, dealRM.Id, lead.Id, true));

                dealMI.CreatePlan(qMI, qMI.DraftPlans[0]);
                dealRM.CreatePlan(qRM, qRM.DraftPlans[0]);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Due Payment indipendenti", "Crea FinancialClient, payMI (Recurring 99) e payRM (Recurring 79).", "Financial", async () =>
            {
                finClient = new FinancialClient { CommercialLeadId = lead!.Id, BillingName = "Giulia Neri" };
                _state.FinancialClients.Add(finClient);

                payMI = new Payment { ClientId = finClient.Id, DealId = dealMI!.Id, QuotationId = qMI!.Id, PaymentType = "Recurring", VatRate = 22 };
                payMI.Products.Add(new SimplifiedProduct { Name = "Canone MI", Price = 99m });
                _state.Payments.Add(payMI);

                payRM = new Payment { ClientId = finClient.Id, DealId = dealRM!.Id, QuotationId = qRM!.Id, PaymentType = "Recurring", VatRate = 22 };
                payRM.Products.Add(new SimplifiedProduct { Name = "Canone RM", Price = 79m });
                _state.Payments.Add(payRM);

                Emit(new PaymentCreatedEvent(payMI.Id, dealMI.Id));
                Emit(new PaymentCreatedEvent(payRM.Id, dealRM.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Simula ritiri - oggetti in deposito per ogni Deal", "20 oggetti depositati a MI (DealId=MI), 15 a RM (DealId=RM). Plan aggiornati. Deal.Activate.", "Execution", async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    var obj = new PhysicalObject { Name = $"mi-{i}", Volume = 1m, DealId = dealMI!.Id, LeadId = lead!.Id };
                    obj.StockDirectly(whMI!.Id);
                    _state.Objects.Add(obj);
                    objsMI.Add(obj);
                }
                for (int i = 0; i < 15; i++)
                {
                    var obj = new PhysicalObject { Name = $"rm-{i}", Volume = 1m, DealId = dealRM!.Id, LeadId = lead!.Id };
                    obj.StockDirectly(whRM!.Id);
                    _state.Objects.Add(obj);
                    objsRM.Add(obj);
                }
                dealMI!.Activate();
                dealRM!.Activate();
                dealMI.TryCloseIfNoObjectsRemaining(20);
                dealRM.TryCloseIfNoObjectsRemaining(15);
                Emit(new DealActivatedEvent(dealMI.Id));
                Emit(new DealActivatedEvent(dealRM.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Consegna totale - stessa destinazione Firenze", "Due Quotation di consegna (una per Deal) stessa destinazione Firenze, SelectedObjectIds distinti.", "Commercial", async () =>
            {
                var addrFI = new Address("50100", null);
                qConsMI = new Quotation { DealId = dealMI!.Id, IsInitial = false };
                qConsMI.Services.Add(new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = addrFI,
                    SelectedObjectIds = objsMI.Select(o => o.Id).ToList()
                });
                qConsMI.Products.Add(new Product { Name = "Consegna MI->FI", Price = 300m });
                dealMI.Quotations.Add(qConsMI);

                qConsRM = new Quotation { DealId = dealRM!.Id, IsInitial = false };
                qConsRM.Services.Add(new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = addrFI,
                    SelectedObjectIds = objsRM.Select(o => o.Id).ToList()
                });
                qConsRM.Products.Add(new Product { Name = "Consegna RM->FI", Price = 350m });
                dealRM.Quotations.Add(qConsRM);

                qConsMI.Finalize();
                qConsRM.Finalize();
                qConsMI.Services[0].AccettaServizio();
                qConsRM.Services[0].AccettaServizio();
                qConsMI.Services[0].SegnaComePronto();
                qConsRM.Services[0].SegnaComePronto();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: Due WO e due Mission (partenze diverse, stessa destinazione)", "WO-MI e WO-RM. Due Planning distinti con Mission specifiche. Programma+AvviaEsecuzione.", "Operational", async () =>
            {
                var svcMI = qConsMI!.Services[0];
                var svcRM = qConsRM!.Services[0];
                var woMI = new WorkOrder { Type = WorkOrderType.Commercial, ServiceBookedId = svcMI.Id, ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[0].Id) };
                var woRM = new WorkOrder { Type = WorkOrderType.Commercial, ServiceBookedId = svcRM.Id, ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[1].Id) };
                _state.WorkOrders.Add(woMI);
                _state.WorkOrders.Add(woRM);
                svcMI.WorkOrderId = woMI.Id;
                svcRM.WorkOrderId = woRM.Id;
                woMI.ServizioPronto("Commercial");
                woRM.ServizioPronto("Commercial");

                var planMI = new Planning { Date = DateTime.Today.AddDays(10) };
                var teamMI = new PlanningTeam { OperatorIds = [_state.Operators[0].Id] };
                planMI.Teams.Add(teamMI);
                planMI.AddMission(teamMI.Id, [new ServiceRef(woMI.Id, 100)], [_state.Vehicles[0].Id]);

                var planRM = new Planning { Date = DateTime.Today.AddDays(10) };
                var teamRM = new PlanningTeam { OperatorIds = [_state.Operators[3].Id] };
                planRM.Teams.Add(teamRM);
                planRM.AddMission(teamRM.Id, [new ServiceRef(woRM.Id, 100)], [_state.Vehicles[2].Id]);

                _state.Plannings.Add(planMI);
                _state.Plannings.Add(planRM);

                woMI.Programma("RespOps");
                woRM.Programma("RespOps");
                woMI.AvviaEsecuzione("Execution");
                woRM.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Due Shift convergono a Firenze", "shiftMI (da Milano) e shiftRM (da Roma). Ogni Shift ha ServiceEntry Consegna con proprio DealId.", "Execution", async () =>
            {
                var planningMI = _state.Plannings[^2];
                var planningRM = _state.Plannings[^1];
                var woMI = _state.WorkOrders.First(w => w.ServiceBookedId == qConsMI!.Services[0].Id);
                var woRM = _state.WorkOrders.First(w => w.ServiceBookedId == qConsRM!.Services[0].Id);

                var shiftMI = new Shift { MissionId = planningMI.Missions[0].Id, Date = planningMI.Date, Mission = new MissionData([_state.Operators[0].FullName], [_state.Vehicles[0].Name], [], "09:00-14:00"), Resources = new ShiftResources([_state.Operators[0].Id], [_state.Vehicles[0].Id], []) };
                shiftMI.AddServiceEntry(woMI.Id, dealMI!.Id, lead!.Id, ServiceEntryType.Consegna, new ClientData("Giulia Neri", "+39 333 0303"));
                _state.Shifts.Add(shiftMI);

                var shiftRM = new Shift { MissionId = planningRM.Missions[0].Id, Date = planningRM.Date, Mission = new MissionData([_state.Operators[3].FullName], [_state.Vehicles[2].Name], [], "10:00-14:00"), Resources = new ShiftResources([_state.Operators[3].Id], [_state.Vehicles[2].Id], []) };
                shiftRM.AddServiceEntry(woRM.Id, dealRM!.Id, lead.Id, ServiceEntryType.Consegna, new ClientData("Giulia Neri", "+39 333 0303"));
                _state.Shifts.Add(shiftRM);

                shiftMI.Start();
                shiftRM.Start();
                foreach (var obj in objsMI) { obj.LoadFromWarehouse(shiftMI.MissionId); obj.Deliver(shiftMI.MissionId); }
                foreach (var obj in objsRM) { obj.LoadFromWarehouse(shiftRM.MissionId); obj.Deliver(shiftRM.MissionId); }
                shiftMI.ServiceEntries[0].Complete("Giulia [firma MI->FI]");
                shiftRM.ServiceEntries[0].Complete("Giulia [firma RM->FI]");
                shiftMI.Complete();
                shiftRM.Complete();
                Emit(new OperationCompletedEvent(shiftMI.Id, woMI.Id));
                Emit(new OperationCompletedEvent(shiftRM.Id, woRM.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Chiusura indipendente dei Deal", "Deal-MI TryClose -> Concluded (0 oggetti). Deal-RM TryClose -> Concluded (0 oggetti). Lead ricalcola status.", "Commercial", async () =>
            {
                var woMI = _state.WorkOrders.First(w => w.ServiceBookedId == qConsMI!.Services[0].Id);
                var woRM = _state.WorkOrders.First(w => w.ServiceBookedId == qConsRM!.Services[0].Id);
                woMI.VerificaEConcludi("RespOps");
                woRM.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woMI.Id, qConsMI!.Services[0].Id, 20m, false, "Consegna MI->FI"));
                Emit(new ServizioCompletatoEvent(woRM.Id, qConsRM!.Services[0].Id, 15m, false, "Consegna RM->FI"));

                dealMI!.TryCloseIfNoObjectsRemaining(0);
                dealRM!.TryCloseIfNoObjectsRemaining(0);
                if (dealMI.Status == DealStatus.Concluded) Emit(new DealConcludedEvent(dealMI.Id, lead!.Id));
                if (dealRM.Status == DealStatus.Concluded) Emit(new DealConcludedEvent(dealRM.Id, lead!.Id));

                lead!.RecalculateStatus([dealMI.Status, dealRM.Status]);
                Emit(new LeadStatusChangedEvent(lead.Id, lead.Status.ToString()));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Addebiti canone mensile eseguiti", "payMI.AddCharge(99) + payRM.AddCharge(79). Execute entrambi.", "Financial", async () =>
            {
                var cMI = payMI!.AddCharge(99m); payMI.ExecuteCharge(cMI.Id);
                var cRM = payRM!.AddCharge(79m); payRM.ExecuteCharge(cRM.Id);
                Emit(new ChargeExecutedEvent(payMI.Id, cMI.Id, 99m));
                Emit(new ChargeExecutedEvent(payRM.Id, cRM.Id, 79m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-10: Trasbordo non programmato
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC10_TrasbordoSteps()
    {
        Lead? leadX = null;
        Deal? dealX = null;
        ServiceBooked? svcRitiroX = null;
        WorkOrder? woRitiroComm = null;
        WorkOrder? woTrasferimentoOp = null;
        Planning? planning = null;
        Shift? shiftA = null;
        Shift? shiftB = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> objects = new();

        return
        [
            new("Step 1: Setup - Deal X con ritiro programmato", "Lead X, Deal X OneOff. Quotation con Ritiro. WO Commercial creato e Programmato.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                leadX = new Lead { Personal = new Personal("Cliente", "X", "x@test.com", "+39 333 0404") };
                _state.Leads.Add(leadX);

                dealX = new Deal { LeadId = leadX.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(dealX);
                leadX.AddDeal(dealX.Id);
                dealX.Qualify(); dealX.EnterNegotiation(); dealX.Convert();
                leadX.MarkConverted();

                var qX = new Quotation { DealId = dealX.Id };
                svcRitiroX = new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("20100", _state.Areas[0].Id) };
                qX.Services.Add(svcRitiroX);
                qX.Products.Add(new Product { Name = "Ritiro Cliente X", Price = 450m });
                dealX.Quotations.Add(qX);
                qX.Finalize();
                svcRitiroX.AccettaServizio();
                svcRitiroX.SegnaComePronto();

                woRitiroComm = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcRitiroX.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id)
                };
                _state.WorkOrders.Add(woRitiroComm);
                svcRitiroX.WorkOrderId = woRitiroComm.Id;
                Emit(new WorkOrderCreatedEvent(woRitiroComm.Id, "Commercial", "Ritiro"));
                woRitiroComm.ServizioPronto("Commercial");
                woRitiroComm.Programma("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Mission A con furgone piccolo", "Planning. Mission A con ServiceRef al WO Commerciale. Team A con furgone piccolo.", "Operational", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(3) };
                var teamA = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(teamA);
                planning.AddMission(teamA.Id, [new ServiceRef(woRitiroComm!.Id, 100)], [_state.Vehicles[1].Id]); // Furgone B (piccolo)
                _state.Plannings.Add(planning);

                woRitiroComm.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Shift A avviato, inizio ritiro", "Shift A. Entry.Start. Ritiro parte - ma il veicolo si riempie prima del previsto (sottostimato).", "Execution", async () =>
            {
                shiftA = new Shift
                {
                    MissionId = planning!.Missions[0].Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[1].Name], [], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[1].Id], [])
                };
                var entryA = shiftA.AddServiceEntry(woRitiroComm!.Id, dealX!.Id, leadX!.Id, ServiceEntryType.Ritiro, new ClientData("Cliente X", "+39 333 0404"));
                _state.Shifts.Add(shiftA);
                shiftA.Start();
                // entryA.Start() rimosso (DDD5 2026-04-14)

                for (int i = 0; i < 25; i++)
                {
                    var obj = new PhysicalObject { Name = $"X-{i}", Volume = 0.8m, DealId = dealX.Id, LeadId = leadX.Id };
                    obj.PickUp(shiftA.MissionId);
                    obj.LoadOnVehicle(shiftA.MissionId);
                    _state.Objects.Add(obj);
                    objects.Add(obj);
                }
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Planner crea WO Operational di trasbordo a runtime", "Crea WO Operational Trasferimento (no ServiceBookedId). Aggiungilo a una nuova Mission B con furgone grande.", "Operational", async () =>
            {
                woTrasferimentoOp = new WorkOrder
                {
                    Type = WorkOrderType.Operational,
                    ServiceBookedId = null,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Trasferimento, false, false, _state.Areas[0].Id)
                };
                _state.WorkOrders.Add(woTrasferimentoOp);
                Emit(new WorkOrderCreatedEvent(woTrasferimentoOp.Id, "Operational", "Trasferimento"));
                woTrasferimentoOp.ServizioPronto("Planner");
                woTrasferimentoOp.Programma("Planner");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Mission B con furgone grande, include WO operativo", "Nuova PlanningTeam B. Mission B con serviceRefs al WO operativo.", "Operational", async () =>
            {
                var teamB = new PlanningTeam { OperatorIds = [_state.Operators[2].Id] };
                planning!.Teams.Add(teamB);
                planning.AddMission(teamB.Id, [new ServiceRef(woTrasferimentoOp!.Id, 100)], [_state.Vehicles[0].Id]); // Furgone A (grande)

                woTrasferimentoOp.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: ShiftA registra Task Trasbordo (trasversale)", "ShiftA aggiunge Task Trasbordo con ServiceEntryId=null e Notes='Scarico su furgone grande'.", "Execution", async () =>
            {
                var trasbordoA = shiftA!.AddTask(TaskType.Trasbordo, null);
                trasbordoA.Notes = "Scarico da furgone piccolo a furgone grande";
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: ShiftB avviato, registra Task Trasbordo speculare", "Crea ShiftB per Mission B. Task Trasbordo con Notes='Carico su furgone grande'. Oggetti restano OnVehicle (cambia solo veicolo).", "Execution", async () =>
            {
                shiftB = new Shift
                {
                    MissionId = planning!.Missions[1].Id,
                    Date = planning.Date,
                    Mission = new MissionData([_state.Operators[2].FullName], [_state.Vehicles[0].Name], [], "10:00-14:00"),
                    Resources = new ShiftResources([_state.Operators[2].Id], [_state.Vehicles[0].Id], [])
                };
                _state.Shifts.Add(shiftB);
                shiftB.Start();
                var trasbordoB = shiftB.AddTask(TaskType.Trasbordo, null);
                trasbordoB.Notes = "Carico su furgone grande dal piccolo";
                // Oggetti restano OnVehicle - non cambia Status
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: ShiftA completa (consegnato il carico al furgone grande)", "entryA.Complete firma. ShiftA.Complete. Emit OperationCompletedEvent per WO commerciale.", "Execution", async () =>
            {
                var entryA = shiftA!.ServiceEntries[0];
                entryA.Complete("Cliente X [firma ritiro - trasbordo a furgone grande]");
                shiftA.Complete();
                Emit(new OperationCompletedEvent(shiftA.Id, woRitiroComm!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: ShiftB porta oggetti a magazzino e completa", "Oggetti UnloadToWarehouse dal furgone grande. ShiftB.Complete. WO Operational completa esecuzione.", "Execution", async () =>
            {
                foreach (var obj in objects)
                {
                    obj.UnloadToWarehouse(warehouse!.Id, shiftB!.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "OnWarehouse", "Post-trasbordo"));
                }
                shiftB!.Complete();
                Emit(new OperationCompletedEvent(shiftB.Id, woTrasferimentoOp!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: WO commerciale e WO operativo chiudono indipendenti", "woRitiroComm.VerificaEConcludi e woTrasferimentoOp.VerificaEConcludi. ServizioCompletato per WO commerciale.", "Operational", async () =>
            {
                woRitiroComm!.VerificaEConcludi("RespOps");
                woTrasferimentoOp!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woRitiroComm.Id, svcRitiroX!.Id, objects.Sum(o => o.Volume), false, "Ritiro completato con trasbordo operativo"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-11: Smaltimento parziale + consegna da deposito attivo
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC11_SmaltimentoSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? qSD = null;
        ServiceBooked? svcSmalt = null;
        ServiceBooked? svcConsegna = null;
        WorkOrder? woSmalt = null;
        WorkOrder? woCons = null;
        Planning? planning = null;
        Shift? shift = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> toDispose = new();
        List<PhysicalObject> toDeliver = new();

        return
        [
            new("Step 1: Setup - Deal Active con 30 oggetti", "Deal Recurring Active, 30 oggetti in deposito Milano. ActivePlan 30m3.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Cliente", "SD", "sd@test.com", "+39 333 0505") };
                _state.Leads.Add(lead);

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                lead.MarkConverted();
                deal.Qualify(); deal.EnterNegotiation(); deal.Convert();

                var qInit = new Quotation { DealId = deal.Id, IsInitial = true, Status = QuotationStatus.Completed };
                qInit.DraftPlans.Add(new DraftPlan { MonthlyFee = 129m, EstimatedM3 = 30m });
                deal.Quotations.Add(qInit);
                deal.CreatePlan(qInit, qInit.DraftPlans[0]);
                deal.ActivePlan!.ObjectCount = 30;
                deal.Activate();

                for (int i = 0; i < 30; i++)
                {
                    var obj = new PhysicalObject { Name = i < 5 ? $"scatolone-{i}" : i < 8 ? (i == 5 ? "divano" : $"sedia{i-5}") : $"altro-{i}", Volume = 1m, DealId = deal.Id, LeadId = lead.Id };
                    obj.StockDirectly(warehouse.Id);
                    _state.Objects.Add(obj);
                    if (i < 5) toDispose.Add(obj);
                    else if (i < 8) toDeliver.Add(obj);
                }
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: Quotation con ServiceBooked Smaltimento + Consegna", "Crea qSD con 2 ServiceBooked: Smaltimento (5 scatoloni) e Consegna (1 divano + 2 sedie).", "Commercial", async () =>
            {
                qSD = new Quotation { DealId = deal!.Id, IsInitial = false };
                svcSmalt = new ServiceBooked
                {
                    Type = ServiceBookedType.Smaltimento,
                    SelectedObjectIds = toDispose.Select(o => o.Id).ToList()
                };
                svcConsegna = new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    SelectedObjectIds = toDeliver.Select(o => o.Id).ToList()
                };
                qSD.Services.Add(svcSmalt);
                qSD.Services.Add(svcConsegna);
                qSD.Products.Add(new Product { Name = "Smaltimento 5 scatoloni", Price = 150m });
                qSD.Products.Add(new Product { Name = "Consegna 3 mobili", Price = 80m });
                deal!.Quotations.Add(qSD);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Accetta Quotation", "qSD.Finalize. Entrambi i ServiceBooked AccettaServizio -> SegnaComePronto.", "Commercial", async () =>
            {
                qSD!.Finalize();
                svcSmalt!.AccettaServizio(); svcSmalt.SegnaComePronto();
                svcConsegna!.AccettaServizio(); svcConsegna.SegnaComePronto();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Due WorkOrder (Smaltimento + Consegna)", "Crea woSmalt e woCons. ServizioPronto entrambi.", "Operational", async () =>
            {
                woSmalt = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcSmalt!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Smaltimento, false, false, _state.Areas[0].Id)
                };
                woCons = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcConsegna!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[0].Id)
                };
                _state.WorkOrders.Add(woSmalt);
                _state.WorkOrders.Add(woCons);
                svcSmalt.WorkOrderId = woSmalt.Id;
                svcConsegna.WorkOrderId = woCons.Id;
                Emit(new WorkOrderCreatedEvent(woSmalt.Id, "Commercial", "Smaltimento"));
                Emit(new WorkOrderCreatedEvent(woCons.Id, "Commercial", "Consegna"));
                woSmalt.ServizioPronto("Commercial");
                woCons.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Mission unica con due ServiceRef (60/40)", "Planning. Mission con ServiceRef a woSmalt 60% e woCons 40%. WO entrambi Programma+AvviaEsecuzione.", "Operational", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(3) };
                var team = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planning.Teams.Add(team);
                planning.AddMission(team.Id, [new ServiceRef(woSmalt!.Id, 60), new ServiceRef(woCons!.Id, 40)], [_state.Vehicles[0].Id]);
                _state.Plannings.Add(planning);

                woSmalt.Programma("RespOps"); woSmalt.AvviaEsecuzione("Execution");
                woCons.Programma("RespOps"); woCons.AvviaEsecuzione("Execution");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: WarehouseOperation OUT (8 oggetti)", "WarehouseOperation OUT per 5 smaltimento + 3 consegna. Start+Complete.", "Execution", async () =>
            {
                var whOut = new WarehouseOperation
                {
                    WarehouseId = warehouse!.Id,
                    MissionId = planning!.Missions[0].Id,
                    OperationType = "OUT",
                    ObjectIds = toDispose.Concat(toDeliver).Select(o => o.Id).ToList()
                };
                whOut.Start();
                foreach (var obj in toDispose.Concat(toDeliver))
                    obj.LoadFromWarehouse(planning.Missions[0].Id);
                whOut.Complete();
                _state.WarehouseOperations.Add(whOut);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Shift con 2 ServiceEntry (Smaltimento + Consegna)", "Shift con entrySmalt e entryCons. shift.Start, entrambe Start.", "Execution", async () =>
            {
                shift = new Shift
                {
                    MissionId = planning!.Missions[0].Id,
                    Date = planning.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-13:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                shift.AddServiceEntry(woSmalt!.Id, deal!.Id, lead!.Id, ServiceEntryType.Smaltimento, new ClientData("WeTacoo", "02-0000"));
                shift.AddServiceEntry(woCons!.Id, deal.Id, lead.Id, ServiceEntryType.Consegna, new ClientData("WeTacoo", "02-0000"));
                _state.Shifts.Add(shift);
                shift.Start();
                // shift.ServiceEntries[0].Start() rimosso (DDD5 2026-04-14)
                // shift.ServiceEntries[1].Start() rimosso (DDD5 2026-04-14)
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: Oggetti Disposed (smaltimento)", "I 5 scatoloni passano Dispose() -> Disposed (terminale).", "Execution", async () =>
            {
                foreach (var obj in toDispose)
                {
                    obj.Dispose(shift!.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "Disposed", "Smaltimento"));
                }
                shift!.ServiceEntries[0].Complete("Ricevuta centro smaltimento #12345");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Oggetti Delivered (consegna)", "I 3 mobili passano Deliver() -> Delivered (terminale).", "Execution", async () =>
            {
                foreach (var obj in toDeliver)
                {
                    obj.Deliver(shift!.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", "Delivered", "Consegna"));
                }
                shift!.ServiceEntries[1].Complete("Firma cliente consegna");
                shift.Complete();
                Emit(new OperationCompletedEvent(shift.Id, woSmalt!.Id));
                Emit(new OperationCompletedEvent(shift.Id, woCons!.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: WO chiusura e Plan aggiornato (30 -> 22)", "Entrambi WO VerificaEConcludi. ServizioCompletato. ActivePlan.UpdateAfterPartialDelivery(8, 8m).", "Operational", async () =>
            {
                woSmalt!.VerificaEConcludi("RespOps");
                woCons!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woSmalt.Id, svcSmalt!.Id, 5m, false, "Smaltimento 5 scatoloni"));
                Emit(new ServizioCompletatoEvent(woCons.Id, svcConsegna!.Id, 3m, false, "Consegna 3 mobili"));
                deal!.ActivePlan!.UpdateAfterPartialDelivery(8, 8m);
                var remaining = _state.Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse);
                deal.TryCloseIfNoObjectsRemaining(remaining);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-12: Self-service con sorpresa (volume eccede slot)
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC12_SelfServiceSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? quotation = null;
        ServiceBooked? svc = null;
        WorkOrder? wo = null;
        Planning? planning = null;
        Shift? shift = null;
        Payment? payment = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> objects = new();

        return
        [
            new("Step 1: Cliente acquista slot self-service 8m3", "Lead+Deal Recurring. Quotation con ServiceBooked Ritiro, slot 8m3 prezzo 49m.", "Commercial", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Cliente", "Self", "self@test.com", "+39 333 0606") };
                _state.Leads.Add(lead);

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                deal.Qualify(); deal.EnterNegotiation();

                quotation = new Quotation { DealId = deal.Id, IsInitial = true };
                svc = new ServiceBooked
                {
                    Type = ServiceBookedType.Ritiro,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    Notes = "Slot 8m3 venduto - self-service"
                };
                quotation.Services.Add(svc);
                quotation.DraftPlans.Add(new DraftPlan { MonthlyFee = 49m, EstimatedM3 = 8m });
                quotation.Products.Add(new Product { Name = "Slot self-service 8m3", Price = 49m });
                deal.Quotations.Add(quotation);

                quotation.Finalize();
                deal.Convert();
                lead.MarkConverted();
                deal.CreatePlan(quotation, quotation.DraftPlans[0]);

                svc.AccettaServizio();
                svc.SegnaComePronto();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Cliente Self" };
                _state.FinancialClients.Add(finClient);
                payment = new Payment { ClientId = finClient.Id, DealId = deal.Id, QuotationId = quotation.Id, PaymentType = "Recurring", VatRate = 22 };
                payment.Products.Add(new SimplifiedProduct { Name = "Slot 8m3", Price = 49m });
                _state.Payments.Add(payment);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: WorkOrder self-service (IsAutonomous=true)", "WO Commercial con ServiceType IsAutonomous=true. ServizioPronto -> DaProgrammare -> Programmato.", "Operational", async () =>
            {
                wo = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svc!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, true, _state.Areas[0].Id),
                    EstimatedVolume = 8m
                };
                _state.WorkOrders.Add(wo);
                svc.WorkOrderId = wo.Id;
                Emit(new WorkOrderCreatedEvent(wo.Id, "Commercial", "Ritiro self-service"));
                wo.ServizioPronto("Commercial");
                wo.Programma("RespOps");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Shift autonomo (no veicolo, magazziniere)", "Shift IsAutonomous=true, no veicoli. Team = magazziniere solo. wo.AvviaEsecuzione.", "Execution", async () =>
            {
                planning = new Planning { Date = DateTime.Today.AddDays(2) };
                var team = new PlanningTeam { OperatorIds = [_state.Operators[0].Id] };
                planning.Teams.Add(team);
                planning.AddMission(team.Id, [new ServiceRef(wo!.Id, 100)], []);
                _state.Plannings.Add(planning);

                shift = new Shift
                {
                    MissionId = planning.Missions[0].Id,
                    Date = planning.Date,
                    
                    Mission = new MissionData([_state.Operators[0].FullName], [], [], "10:00-12:00"),
                    Resources = new ShiftResources([_state.Operators[0].Id], [], [])
                };
                var entry = shift.AddServiceEntry(wo!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Cliente Self", "+39 333 0606"));
                _state.Shifts.Add(shift);

                wo.AvviaEsecuzione("Execution");
                shift.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Cliente arriva - 34 oggetti (volume reale 11m3)", "Magazziniere censisce 34 oggetti totali: volume reale 11m3 (supera slot 8m3). Task Censimento.", "Execution", async () =>
            {
                var entry = shift!.ServiceEntries[0];
                for (int i = 0; i < 34; i++)
                {
                    var obj = new PhysicalObject { Name = $"scatola-{i}", Volume = 0.32m, DealId = deal!.Id, LeadId = lead!.Id };
                    obj.PickUp(shift.MissionId);
                    _state.Objects.Add(obj);
                    objects.Add(obj);
                }
                shift.AddTask(TaskType.Censimento, entry.Id, objects.Select(o => o.Id).ToList());
                wo!.ActualVolume = 11m;
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 5: Volume eccede - WO MettiInPausa", "wo.MettiInPausa('volume 11m3 supera slot 8m3'). Emit RichiedeInterventoEvent.", "Operational", async () =>
            {
                wo!.MettiInPausa("Volume reale 11m3 supera slot 8m3 - attende decisione Sales");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "InExecution", "Paused", "Volume eccede"));
                Emit(new RichiedeInterventoEvent(wo.Id, svc!.Id, "Slot 8m3 superato - reale 11m3"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: ServiceBooked DaCompletare + Quotation ToVerify", "svc.RichiedeIntervento (cascade PlaygroundState). quotation.MarkToVerify.", "Commercial", async () =>
            {
                if (quotation!.Status == QuotationStatus.Finalized) quotation.MarkToVerify();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }, BranchPointNote: "UC-12 D3 elenca 3 outcome: (a) cliente accetta nuovo prezzo [flusso seguito dal Riepilogo stati, implementato qui sotto], (b) cliente riduce oggetti (ChiusuraAnticipata parziale), (c) cliente rifiuta tutto (cancellazione servizio come UC-13)."),

            new("Step 7: Sales propone nuovo slot 11m3 - cliente accetta", "Sales rinegozia: plan 11m3 69m. svc.InterventoRisolto. quotation.Verify (ToVerify -> Finalized).", "Commercial", async () =>
            {
                deal!.ActivePlan!.CurrentM3 = 11m;
                deal.ActivePlan.MonthlyFee = 69m;
                deal.ActivePlan.History.Add($"{DateTime.UtcNow:u} Slot adeguato: 8m3 -> 11m3, fee 49 -> 69");
                svc!.InterventoRisolto();
                quotation!.Verify();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: WO InterventoRisoltoRiprendi", "wo.InterventoRisoltoRiprendi -> InEsecuzione. Emit InterventoRisoltoEvent.", "Commercial", async () =>
            {
                wo!.InterventoRisoltoRiprendi("Cliente accetta nuovo prezzo, slot aggiornato a 11m3");
                Emit(new WorkOrderStatusChangedEvent(wo.Id, "Paused", "InExecution", "Intervento risolto - riprendi"));
                Emit(new InterventoRisoltoEvent(wo.Id, "riprendi", "Prezzo e slot aggiornati"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: StockDirectly - oggetti in magazzino (no veicolo)", "Self-service: PickedUp -> StockDirectly non disponibile, usiamo UnloadToWarehouse dopo LoadOnVehicle (convenzione). In effetti self-service salta veicolo: forziamo LoadOnVehicle+UnloadToWarehouse per coerenza.", "Execution", async () =>
            {
                foreach (var obj in objects)
                {
                    obj.LoadOnVehicle(shift!.MissionId); // PickedUp -> OnVehicle (fittizio per self-service)
                    obj.UnloadToWarehouse(warehouse!.Id, shift.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "PickedUp", "OnWarehouse", "Stoccaggio self-service"));
                }
                var whIn = new WarehouseOperation
                {
                    WarehouseId = warehouse!.Id,
                    MissionId = shift!.MissionId,
                    OperationType = "IN",
                    ObjectIds = objects.Select(o => o.Id).ToList()
                };
                whIn.Start(); whIn.Complete();
                _state.WarehouseOperations.Add(whIn);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Shift completa e WO chiude", "entry.Complete firma digitale. shift.Complete. OperationCompletedEvent. wo.VerificaEConcludi.", "Execution", async () =>
            {
                var entry = shift!.ServiceEntries[0];
                entry.Complete("Cliente Self [firma digitale]");
                shift.Complete();
                Emit(new OperationCompletedEvent(shift.Id, wo!.Id));
                wo.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(wo.Id, svc!.Id, 11m, true, "Volume eccedente slot venduto - riconciliato"));
                svc!.CompletionData = new CompletionRecord(11m, 34, "Volume eccedente slot venduto", DateTime.UtcNow);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 11: Primo canone 69m", "Payment aggiornato: aggiunge Product 'Adeguamento slot 8->11'. AddCharge(69). Execute.", "Financial", async () =>
            {
                payment!.Products.Add(new SimplifiedProduct { Name = "Adeguamento slot 8m3->11m3", Price = 20m });
                var charge = payment.AddCharge(69m);
                charge.Execute();
                Emit(new ChargeExecutedEvent(payment.Id, charge.Id, 69m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    // ══════════════════════════════════════════════════════
    // UC-13: Cancellazione a meta' esecuzione
    // ══════════════════════════════════════════════════════
    public List<ScenarioStep> GetUC13_CancellazioneMetaSteps()
    {
        Lead? lead = null;
        Deal? deal = null;
        Quotation? qRitiro = null;
        Quotation? qConsegna = null;
        ServiceBooked? svcRitiro = null;
        ServiceBooked? svcConsegna = null;
        WorkOrder? woRitiro = null;
        WorkOrder? woConsegna = null;
        Planning? planningD1 = null;
        Planning? planningD2_cancelled = null;
        Shift? shiftD1 = null;
        Payment? penaltyPayment = null;
        FinancialClient? finClient = null;
        Warehouse? warehouse = null;
        List<PhysicalObject> objectsRitirati = new();

        return
        [
            new("Step 1: Setup - Deal, Quotation ritiro grande (200 oggetti)", "Lead+Deal OneOff. Quotation con ServiceBooked Ritiro (200 oggetti stimati). Finalize+Convert+AccettaServizio+SegnaComePronto.", "Setup", async () =>
            {
                warehouse = _state.Warehouses.First(w => w.Name.Contains("Milano"));
                lead = new Lead { Personal = new Personal("Cliente", "Cancella", "cancel@test.com", "+39 333 0707") };
                _state.Leads.Add(lead);

                deal = new Deal { LeadId = lead.Id, AreaId = _state.Areas[0].Id };
                _state.Deals.Add(deal);
                lead.AddDeal(deal.Id);
                deal.Qualify(); deal.EnterNegotiation(); deal.Convert();
                lead.MarkConverted();

                qRitiro = new Quotation { DealId = deal.Id, IsInitial = true };
                svcRitiro = new ServiceBooked { Type = ServiceBookedType.Ritiro, ServiceAddress = new Address("20100", _state.Areas[0].Id) };
                qRitiro.Services.Add(svcRitiro);
                qRitiro.Products.Add(new Product { Name = "Ritiro 200 oggetti", Price = 4000m });
                deal.Quotations.Add(qRitiro);
                qRitiro.Finalize();
                svcRitiro.AccettaServizio();
                svcRitiro.SegnaComePronto();

                finClient = new FinancialClient { CommercialLeadId = lead.Id, BillingName = "Cliente Cancella" };
                _state.FinancialClients.Add(finClient);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 2: WorkOrder e Mission multi-giorno", "Crea woRitiro. ServizioPronto -> Programma. Planning giorno 1 e giorno 2 (+3 se serve).", "Operational", async () =>
            {
                woRitiro = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcRitiro!.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Ritiro, false, false, _state.Areas[0].Id),
                    EstimatedVolume = 80m
                };
                _state.WorkOrders.Add(woRitiro);
                svcRitiro.WorkOrderId = woRitiro.Id;
                Emit(new WorkOrderCreatedEvent(woRitiro.Id, "Commercial", "Ritiro"));
                woRitiro.ServizioPronto("Commercial");
                woRitiro.Programma("RespOps");

                planningD1 = new Planning { Date = DateTime.Today.AddDays(5) };
                var teamD1 = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planningD1.Teams.Add(teamD1);
                planningD1.AddMission(teamD1.Id, [new ServiceRef(woRitiro.Id, 50)], [_state.Vehicles[0].Id]);

                planningD2_cancelled = new Planning { Date = DateTime.Today.AddDays(6) };
                var teamD2 = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planningD2_cancelled.Teams.Add(teamD2);
                planningD2_cancelled.AddMission(teamD2.Id, [new ServiceRef(woRitiro.Id, 50)], [_state.Vehicles[0].Id]);

                _state.Plannings.Add(planningD1);
                _state.Plannings.Add(planningD2_cancelled);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 3: Giorno 1 - Shift esegue, ritira 75 oggetti", "Shift giorno 1. Entry.Start. 75 oggetti PickUp+LoadOnVehicle+UnloadToWarehouse. wo.AvviaEsecuzione.", "Execution", async () =>
            {
                shiftD1 = new Shift
                {
                    MissionId = planningD1!.Missions[0].Id,
                    Date = planningD1.Date,
                    Mission = new MissionData(_state.Operators.Take(2).Select(o => o.FullName).ToList(), [_state.Vehicles[0].Name], [], "09:00-17:00"),
                    Resources = new ShiftResources(_state.Operators.Take(2).Select(o => o.Id).ToList(), [_state.Vehicles[0].Id], [])
                };
                var entry = shiftD1.AddServiceEntry(woRitiro!.Id, deal!.Id, lead!.Id, ServiceEntryType.Ritiro, new ClientData("Cliente Cancella", "+39 333 0707"));
                _state.Shifts.Add(shiftD1);

                woRitiro.AvviaEsecuzione("Execution");
                shiftD1.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)

                for (int i = 0; i < 75; i++)
                {
                    var obj = new PhysicalObject { Name = $"obj-{i}", Volume = 0.4m, DealId = deal.Id, LeadId = lead.Id };
                    obj.PickUp(shiftD1.MissionId);
                    obj.LoadOnVehicle(shiftD1.MissionId);
                    obj.UnloadToWarehouse(warehouse!.Id, shiftD1.MissionId);
                    _state.Objects.Add(obj);
                    objectsRitirati.Add(obj);
                }
                // DDD5 2026-04-14: parziale tracciato via payload chiusura Shift (SE Completed=false, Shift Sospeso)
                var _outcomeD1UC13 = new ServiceEntryOutcome(entry.Id, "parziale", "75/200 oggetti censiti");
                shiftD1.Suspend();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 4: Cliente chiama la sera - cancella il resto", "Commercial riceve cancellazione. Decisione: chiudere lo scope a 75 oggetti (niente piu' ritiri).", "Commercial", async () =>
            {
                // Decisione commerciale — log only
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }, BranchPointNote: "UC-13 ha piu' esiti possibili: (a) chiusura scope a 75 oggetti + consegna totale [flusso implementato qui sotto], (b) rimborso totale al cliente con cancellazione completa, (c) continuazione forzata a scope originale (200). L'outcome cambia la catena Quotation + Payment."),

            new("Step 5: WO ChiusuraAnticipata", "wo.ChiusuraAnticipata('Cancellazione cliente') -> InEsecuzione -> DaVerificare. Emit ChiusuraAnticipataEvent.", "Commercial", async () =>
            {
                woRitiro!.ChiusuraAnticipata("Cancellazione cliente, completato 75 su 200");
                Emit(new WorkOrderStatusChangedEvent(woRitiro.Id, "InExecution", "ToVerify", "Chiusura anticipata"));
                Emit(new ChiusuraAnticipataEvent(woRitiro.Id, svcRitiro!.Id, "Cancellazione cliente"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 6: ServiceBooked ChiudiAVolumeRidotto", "svc.RichiedeIntervento -> DaCompletare. svc.ChiudiAVolumeRidotto -> Completato. CompletionData tracks 75 oggetti.", "Commercial", async () =>
            {
                svcRitiro!.RichiedeIntervento("cancellazione");
                svcRitiro.ChiudiAVolumeRidotto();
                svcRitiro.CompletionData = new CompletionRecord(30m, 75, "Scope ridotto per cancellazione cliente", DateTime.UtcNow);
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 7: Mission giorni 2-3 cancellate", "planningD2_cancelled.Missions[0].IsCancelled = true. ServiceEntry (se ne esistono) Cancel.", "Operational", async () =>
            {
                planningD2_cancelled!.Missions[0].IsCancelled = true;
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 8: WO.VerificaEConcludi", "woRitiro.VerificaEConcludi -> Concluso. Quotation.Complete (Finalized -> Completed).", "Operational", async () =>
            {
                woRitiro!.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woRitiro.Id, svcRitiro!.Id, 30m, true, "75 oggetti ritirati, scope ridotto"));
                if (qRitiro!.Status is QuotationStatus.Finalized or QuotationStatus.ToAdjust) qRitiro.Complete();
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 9: Nuova Quotation Consegna (totale 75 oggetti)", "Crea qConsegna IsInitial=false. ServiceBooked Consegna con SelectedObjectIds=75 oggetti. Finalize+Accetta+Pronto.", "Commercial", async () =>
            {
                qConsegna = new Quotation { DealId = deal!.Id, IsInitial = false };
                svcConsegna = new ServiceBooked
                {
                    Type = ServiceBookedType.Consegna,
                    ServiceAddress = new Address("20100", _state.Areas[0].Id),
                    SelectedObjectIds = objectsRitirati.Select(o => o.Id).ToList(),
                    Notes = "Consegna totale post-cancellazione"
                };
                qConsegna.Services.Add(svcConsegna);
                qConsegna.Products.Add(new Product { Name = "Consegna totale 75 oggetti", Price = 600m });
                deal!.Quotations.Add(qConsegna);
                qConsegna.Finalize();
                svcConsegna.AccettaServizio();
                svcConsegna.SegnaComePronto();

                woConsegna = new WorkOrder
                {
                    Type = WorkOrderType.Commercial,
                    ServiceBookedId = svcConsegna.Id,
                    ServiceType = new ServiceTypeVO(ServiceTypeEnum.Consegna, false, false, _state.Areas[0].Id)
                };
                _state.WorkOrders.Add(woConsegna);
                svcConsegna.WorkOrderId = woConsegna.Id;
                Emit(new WorkOrderCreatedEvent(woConsegna.Id, "Commercial", "Consegna"));
                woConsegna.ServizioPronto("Commercial");
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 10: Planning + Mission + Shift Consegna", "Crea Planning giorno 7, Team+Mission con ServiceRef al woConsegna. Shift con ServiceEntry Consegna. wo.Programma (DaProgrammare -> Programmato).", "Operational", async () =>
            {
                woConsegna!.Programma("RespOps");
                Emit(new WorkOrderStatusChangedEvent(woConsegna.Id, "ToSchedule", "Scheduled", "Programmato via Mission Consegna"));

                var planningConsegna = new Planning { Date = DateTime.Today.AddDays(7) };
                var teamConsegna = new PlanningTeam { OperatorIds = _state.Operators.Take(2).Select(o => o.Id).ToList() };
                planningConsegna.Teams.Add(teamConsegna);
                var missionConsegna = planningConsegna.AddMission(teamConsegna.Id, [new ServiceRef(woConsegna.Id, 100)], [_state.Vehicles[0].Id]);
                missionConsegna.TimeSlot = "09:00-12:00";
                _state.Plannings.Add(planningConsegna);

                var shiftConsegna = new Shift
                {
                    MissionId = missionConsegna.Id,
                    Date = planningConsegna.Date,
                    Mission = new MissionData(teamConsegna.OperatorIds, [_state.Vehicles[0].Id], [], missionConsegna.TimeSlot),
                    Resources = new ShiftResources(teamConsegna.OperatorIds, [_state.Vehicles[0].Id], [])
                };
                shiftConsegna.AddServiceEntry(woConsegna.Id, deal!.Id, lead!.Id, ServiceEntryType.Consegna, new ClientData("Cliente Cancella", "+39 333 0707"));
                _state.Shifts.Add(shiftConsegna);
                Emit(new ShiftCreatedEvent(shiftConsegna.Id, missionConsegna.Id));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 11: Esecuzione Shift Consegna - oggetti Delivered", "shift.Start -> entry.Start. Oggetti LoadFromWarehouse -> Deliver. entry.Complete, shift.Complete. OperationStarted/Completed events. wo CompletaEsecuzione + VerificaEConcludi.", "Execution", async () =>
            {
                var shiftConsegna = _state.Shifts.Last(s => s.ServiceEntries.Any(se => se.ServiceId == woConsegna!.Id));
                var entry = shiftConsegna.ServiceEntries.First();

                woConsegna!.AvviaEsecuzione("Execution");
                Emit(new OperationStartedEvent(shiftConsegna.Id, woConsegna.Id));
                shiftConsegna.Start();
                // entry.Start() rimosso (DDD5 review 2026-04-14: SE senza state machine)

                foreach (var obj in objectsRitirati)
                {
                    obj.LoadFromWarehouse(shiftConsegna.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnWarehouse", obj.Status.ToString(), "LoadFromWarehouse"));
                    obj.Deliver(shiftConsegna.MissionId);
                    Emit(new ObjectStateChangedEvent(obj.Id, "OnVehicle", obj.Status.ToString(), "Deliver"));
                }

                entry.Complete($"Firma_{entry.ClientInfo?.ClientName}_{DateTime.UtcNow:HHmmss}");
                Emit(new ServiceExecutedEvent(shiftConsegna.Id, entry.Id, woConsegna.Id, "Completed"));
                shiftConsegna.Complete();

                Emit(new OperationCompletedEvent(shiftConsegna.Id, woConsegna.Id));
                woConsegna.CompletaEsecuzione("Execution");
                woConsegna.VerificaEConcludi("RespOps");
                Emit(new ServizioCompletatoEvent(woConsegna.Id, svcConsegna!.Id, 30m, false, "Consegna completa post-cancellazione"));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 12: Payment penale cancellazione", "Crea Payment Penale 500m. AddCharge + ExecuteCharge.", "Financial", async () =>
            {
                penaltyPayment = new Payment { ClientId = finClient!.Id, DealId = deal!.Id, PaymentType = "OneOff", VatRate = 22 };
                penaltyPayment.Products.Add(new SimplifiedProduct { Name = "Penale cancellazione", Price = 500m });
                _state.Payments.Add(penaltyPayment);
                Emit(new PaymentCreatedEvent(penaltyPayment.Id, deal.Id));

                var charge = penaltyPayment.AddCharge(500m);
                charge.Notes = "Penale cancellazione cliente";
                penaltyPayment.ExecuteCharge(charge.Id);
                Emit(new ChargeExecutedEvent(penaltyPayment.Id, charge.Id, 500m));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),

            new("Step 13: Deal Concluded", "TryCloseIfNoObjectsRemaining(0) -> Deal Concluded (OneOff). Lead RecalculateStatus. Emit DealConcludedEvent.", "Commercial", async () =>
            {
                deal!.TryCloseIfNoObjectsRemaining(0, allServicesCompleted: true);
                if (deal.Status == DealStatus.Concluded) Emit(new DealConcludedEvent(deal.Id, lead!.Id));
                lead!.RecalculateStatus([deal.Status]);
                Emit(new LeadStatusChangedEvent(lead.Id, lead.Status.ToString()));
                _state.NotifyStateChanged();
                await Task.CompletedTask;
            }),
        ];
    }

    public Dictionary<string, List<ScenarioStep>> GetAllScenarios() => new()
    {
        ["UC-1: Ritiro e deposito standard"] = GetUC1_DepositoStandardSteps(),
        ["UC-2: Trasloco punto-punto"] = GetUC2_TraslocoSteps(),
        ["UC-3: Consegna parziale da deposito attivo"] = GetUC3_ConsegnaParzialeSteps(),
        ["UC-4: Ritiro che dura 3 giorni"] = GetUC4_RitiroMultiGiornoSteps(),
        ["UC-5: Sopralluogo che cambia il preventivo"] = GetUC5_SopralluogoSteps(),
        ["UC-6: Problema in esecuzione che rimbalza al commerciale"] = GetUC6_ProblemaEsecuzioneSteps(),
        ["UC-7: Servizio extra non preventivato"] = GetUC7_ServizioExtraSteps(),
        ["UC-8: Oggetti rifiutati alla consegna"] = GetUC8_OggettiRifiutatiSteps(),
        ["UC-9: Stesso cliente, due depositi in due citta'"] = GetUC9_DueDepositiSteps(),
        ["UC-10: Trasbordo non programmato"] = GetUC10_TrasbordoSteps(),
        ["UC-11: Smaltimento parziale da deposito attivo"] = GetUC11_SmaltimentoSteps(),
        ["UC-12: Self-service con sorpresa"] = GetUC12_SelfServiceSteps(),
        ["UC-13: Cancellazione a meta' esecuzione"] = GetUC13_CancellazioneMetaSteps(),
    };
}

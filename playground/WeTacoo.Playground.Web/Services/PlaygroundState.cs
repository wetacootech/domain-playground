using WeTacoo.Domain.Common;
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

public class PlaygroundState
{
    // Commercial
    public List<Lead> Leads { get; } = [];
    public List<Deal> Deals { get; } = [];
    public List<ProductTemplate> ProductTemplates { get; } = [];
    public List<QuestionTemplate> QuestionTemplates { get; } = [];
    public List<Questionnaire> Questionnaires { get; } = [];
    public List<Coupon> Coupons { get; } = [];
    public List<Salesman> Salesmen { get; } = [];

    // Shared Infrastructure
    public List<ObjectTemplate> ObjectTemplates { get; } = [];
    public List<Area> Areas { get; } = [];

    // Operational
    public List<WorkOrder> WorkOrders { get; } = [];
    public List<Planning> Plannings { get; } = [];
    public List<Slot> Slots { get; } = [];
    public List<Warehouse> Warehouses { get; } = [];
    public List<Vehicle> Vehicles { get; } = [];
    public List<Operator> Operators { get; } = [];
    public List<Asset> Assets { get; } = [];

    // Execution
    public List<Shift> Shifts { get; } = [];
    public List<PhysicalObject> Objects { get; } = [];
    public List<WarehouseOperation> WarehouseOperations { get; } = [];
    public List<Label> Labels { get; } = [];
    public List<Pallet> Pallets { get; } = [];
    public List<VehicleOperation> VehicleOperations { get; } = [];
    public List<ObjectGroup> ObjectGroups { get; } = [];

    // Financial
    public List<FinancialClient> FinancialClients { get; } = [];
    public List<Payment> Payments { get; } = [];

    // Identity
    public List<User> Users { get; } = [];

    // Marketing
    public List<MarketingClient> MarketingClients { get; } = [];

    // Happiness
    public List<HappinessClient> HappinessClients { get; } = [];

    // Shared Infrastructure — Communications
    public List<WeTacoo.Domain.SharedInfrastructure.Email> Emails { get; } = [];

    // Events
    public List<IDomainEvent> EventLog { get; } = [];

    // UI session state (persistente fra navigazioni, reset solo su comando manuale)
    public string? SelectedLeadId { get; set; }
    public HashSet<string> CompletedScenarioSteps { get; } = [];
    // Cache degli scenari: preserva le closure locali (lead/deal/wo/ecc.) cosi' che tornando alla pagina scenari
    // dopo aver eseguito alcuni step si possa continuare con quelli successivi usando le stesse reference.
    public Dictionary<string, List<ScenarioStep>>? CachedScenarios { get; set; }

    public event Action? OnStateChanged;
    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    public void PublishEvent(IDomainEvent evt)
    {
        EventLog.Insert(0, evt);
        ProcessEvent(evt);
        NotifyStateChanged();
    }

    /// <summary>
    /// Event-driven state propagation cross-BC (DDD5 §10c/§10d, review 2026-04-13).
    /// Due state machine indipendenti: ServiceBooked (Commercial) e WorkOrder (Operational).
    /// </summary>
    private void ProcessEvent(IDomainEvent evt)
    {
        switch (evt)
        {
            // ── Commercial -> Operational: servizio pronto -> WorkOrder: InCompletamento -> DaProgrammare
            case ServizioProntoEvent e:
            {
                var wo = WorkOrders.FirstOrDefault(w => w.Id == e.WorkOrderId);
                wo?.ServizioPronto("Commercial");
                break;
            }

            // ── Execution -> Operational: Shift avviato -> WorkOrder: Programmato -> InEsecuzione
            case OperationStartedEvent e:
            {
                if (e.WorkOrderId != null)
                {
                    var wo = WorkOrders.FirstOrDefault(w => w.Id == e.WorkOrderId);
                    wo?.AvviaEsecuzione("Execution");
                }
                break;
            }

            // ── Execution -> Operational: Shift completato -> WorkOrder: InEsecuzione -> DaVerificare
            //   Multi-Mission: il WO resta InEsecuzione finche' TUTTI gli Shift associati sono Completati
            //   Eccezione Sopralluogo (DDD5 §4.8 review 2026-04-17): il WO tipo Sopralluogo salta ToVerify
            //   e va direttamente a Concluded; il ServiceBooked collegato torna ToAccept automaticamente.
            case OperationCompletedEvent e:
            {
                if (e.WorkOrderId != null)
                {
                    var wo = WorkOrders.FirstOrDefault(w => w.Id == e.WorkOrderId);
                    if (wo != null)
                    {
                        var shiftsForWo = Shifts.Where(s => s.ServiceEntries.Any(se => se.ServiceId == wo.Id)).ToList();
                        var allCompleted = shiftsForWo.Count > 0 && shiftsForWo.All(s => s.Status == "Completed");
                        if (allCompleted)
                        {
                            wo.CompletaEsecuzione("Execution");
                            if (wo.Type == WorkOrderType.Sopralluogo)
                            {
                                wo.VerificaEConcludi("Auto (sopralluogo a fine Shift)");
                                var quest = !string.IsNullOrEmpty(wo.Commercial?.QuestionnaireId)
                                    ? Questionnaires.FirstOrDefault(qn => qn.Id == wo.Commercial.QuestionnaireId)
                                    : null;
                                if (quest != null) quest.IsVerified = true;
                                if (!string.IsNullOrEmpty(wo.ServiceBookedId) && !string.IsNullOrEmpty(wo.Commercial?.QuestionnaireId))
                                {
                                    EventLog.Add(new InspectionCompletataEvent(wo.Id, wo.ServiceBookedId, wo.Commercial.QuestionnaireId));
                                    ProcessEvent(EventLog[^1]);
                                }
                            }
                        }
                        // Altrimenti resta InEsecuzione in attesa degli altri Shift
                    }
                }
                break;
            }

            // ── Operational -> Commercial: sopralluogo concluso -> ServiceBooked WaitingInspection -> ToAccept.
            //   Anche il Questionnaire viene marcato verificato (redondante, gia' settato sopra per sicurezza).
            case InspectionCompletataEvent e:
            {
                foreach (var deal in Deals)
                {
                    foreach (var q in deal.Quotations)
                    {
                        var svc = q.Services.FirstOrDefault(s => s.Id == e.ServiceBookedId);
                        if (svc != null)
                        {
                            svc.SopralluogoCompletato();
                            var quest = Questionnaires.FirstOrDefault(qn => qn.Id == e.QuestionnaireId);
                            if (quest != null) quest.IsVerified = true;
                        }
                    }
                }
                break;
            }

            // ── Operational -> Commercial: servizio completato -> ServiceBooked: Pronto -> Completato, then Quotation/Deal cascade
            case ServizioCompletatoEvent e:
            {
                if (e.ServiceBookedId == null) break;
                foreach (var deal in Deals)
                {
                    foreach (var q in deal.Quotations.Where(q => q.IsAccepted))
                    {
                        var svc = q.Services.FirstOrDefault(s => s.Id == e.ServiceBookedId);
                        if (svc == null) continue;

                        svc.ServizioCompletato();
                        if (e.HasDifferences)
                            svc.CompletionData = new CompletionRecord(e.ActualVolume, 0, e.Notes, DateTime.UtcNow);

                        // Check if ALL ServiceBooked for this Quotation are Completato
                        var allDone = q.Services.All(s => s.Status == ServiceBookedStatus.Completed);
                        if (!allDone) break;

                        // Quotation: Finalizzato -> Completato o DaAdeguare
                        if (q.Status == QuotationStatus.Finalized)
                        {
                            if (e.HasDifferences) q.MarkToAdjust();
                            else q.Complete();
                        }

                        // Deal cascade
                        if (deal.Status == DealStatus.Converted)
                        {
                            if (deal.Type == DealType.Recurring)
                            {
                                deal.Activate();
                                PublishEventInternal(new DealActivatedEvent(deal.Id));
                            }
                            else
                            {
                                deal.Conclude();
                                PublishEventInternal(new DealConcludedEvent(deal.Id, deal.LeadId));
                            }
                        }
                        else if (deal.Status == DealStatus.Active)
                        {
                            var allQuotDone = deal.Quotations.Where(qq => qq.IsAccepted)
                                .All(qq => qq.Status is QuotationStatus.Completed or QuotationStatus.ToAdjust);
                            if (allQuotDone)
                            {
                                var objCount = Objects.Count(o => o.DealId == deal.Id && o.Status == ObjectStatus.OnWarehouse);
                                deal.TryCloseIfNoObjectsRemaining(objCount);
                                if (deal.Status == DealStatus.Concluded)
                                    PublishEventInternal(new DealConcludedEvent(deal.Id, deal.LeadId));
                            }
                        }
                        return;
                    }
                }
                break;
            }

            // ── Operational -> Commercial: richiede intervento -> ServiceBooked: Pronto -> DaCompletare
            case RichiedeInterventoEvent e:
            {
                if (e.ServiceBookedId == null) break;
                foreach (var deal in Deals)
                    foreach (var q in deal.Quotations)
                    {
                        var svc = q.Services.FirstOrDefault(s => s.Id == e.ServiceBookedId);
                        svc?.RichiedeIntervento(e.Motivo);
                    }
                break;
            }

        }
    }

    /// <summary>Log event without re-processing (avoids infinite loop)</summary>
    private void PublishEventInternal(IDomainEvent evt)
    {
        EventLog.Insert(0, evt);
    }

    public PlaygroundState()
    {
        Seed();
    }

    public void Reset()
    {
        AggregateRoot.ResetCounter();
        Entity.ResetCounter();
        Leads.Clear(); Deals.Clear(); ProductTemplates.Clear(); QuestionTemplates.Clear();
        Questionnaires.Clear(); Coupons.Clear(); Salesmen.Clear();
        ObjectTemplates.Clear(); Areas.Clear();
        WorkOrders.Clear(); Plannings.Clear(); Slots.Clear(); Warehouses.Clear();
        Vehicles.Clear(); Operators.Clear(); Assets.Clear();
        Shifts.Clear(); Objects.Clear(); WarehouseOperations.Clear(); Labels.Clear();
        Pallets.Clear(); VehicleOperations.Clear(); ObjectGroups.Clear();
        FinancialClients.Clear(); Payments.Clear();
        Users.Clear(); MarketingClients.Clear(); HappinessClients.Clear();
        Emails.Clear();
        EventLog.Clear();
        SelectedLeadId = null;
        CompletedScenarioSteps.Clear();
        CachedScenarios = null;
        Seed();
        NotifyStateChanged();
    }

    // ══════════════════════════════════════════════════
    //  SEED DATA
    // ══════════════════════════════════════════════════
    private void Seed()
    {
        // ── Aree di copertura (Shared Infrastructure) ──
        var areaMilano = new Area { Name = "Milano", City = "Milano", ZipCodes = ["20100", "20121", "20122", "20124", "20129", "20131", "20133", "20135", "20137", "20141"], MinBookingDays = 3, MinDeliveryDays = 5 };
        var areaRoma = new Area { Name = "Roma", City = "Roma", ZipCodes = ["00100", "00118", "00121", "00136", "00144", "00153", "00165", "00185", "00195", "00199"], MinBookingDays = 4, MinDeliveryDays = 7 };
        var areaTorino = new Area { Name = "Torino", City = "Torino", ZipCodes = ["10121", "10122", "10123", "10124", "10125", "10126", "10128", "10129", "10131", "10133"], MinBookingDays = 3, MinDeliveryDays = 5 };
        Areas.AddRange([areaMilano, areaRoma, areaTorino]);

        // ── Magazzini ──
        var whMilano = new Warehouse { Name = "Magazzino Milano Nord", Address = "Via Logistica 1, Pero (MI)", AreaId = areaMilano.Id, Capacity = 500, Status = "Active" };
        var whRoma = new Warehouse { Name = "Magazzino Roma Est", Address = "Via dei Magazzini 22, Roma", AreaId = areaRoma.Id, Capacity = 350, Status = "Active" };
        var whTorino = new Warehouse { Name = "Magazzino Torino Sud", Address = "Strada del Drosso 10, Torino", AreaId = areaTorino.Id, Capacity = 400, Status = "Active" };
        Warehouses.AddRange([whMilano, whRoma, whTorino]);

        // ── Veicoli ──
        Vehicles.AddRange([
            new Vehicle { Name = "Furgone A", Plate = "MI-2501", AreaId = areaMilano.Id, Capacity = 30, Status = "Available" },
            new Vehicle { Name = "Furgone B", Plate = "MI-2502", AreaId = areaMilano.Id, Capacity = 20, Status = "Available" },
            new Vehicle { Name = "Furgone Roma", Plate = "RM-4401", AreaId = areaRoma.Id, Capacity = 25, Status = "Available" },
            new Vehicle { Name = "Furgone Torino", Plate = "TO-1101", AreaId = areaTorino.Id, Capacity = 30, Status = "Available" },
        ]);

        // ── Operatori ──
        var opAdmin = new OperatorUser { Email = "admin@wetacoo.com", Role = "Admin" };
        Users.Add(opAdmin);

        var ops = new (string First, string Last, string Area, string Wh)[]
        {
            ("Marco", "Rossi", areaMilano.Id, whMilano.Id),
            ("Luca", "Bianchi", areaMilano.Id, whMilano.Id),
            ("Giovanni", "Ferrari", areaMilano.Id, whMilano.Id),
            ("Alessandro", "Conti", areaRoma.Id, whRoma.Id),
            ("Matteo", "Ricci", areaRoma.Id, whRoma.Id),
            ("Davide", "Gallo", areaTorino.Id, whTorino.Id),
            ("Andrea", "Moretti", areaTorino.Id, whTorino.Id),
        };
        foreach (var (first, last, areaId, whId) in ops)
        {
            var opUser = new OperatorUser { Email = $"{first.ToLower()}.{last.ToLower()}@wetacoo.com" };
            Users.Add(opUser);
            Operators.Add(new Operator { FirstName = first, LastName = last, AreaId = areaId, IdentityId = opUser.Id, AssignedWarehouseIds = [whId], Status = "Active" });
        }

        // ── Commerciali ──
        var sales = new (string First, string Last)[] { ("Francesca", "Martini"), ("Roberto", "Colombo"), ("Sara", "Mancini") };
        foreach (var (first, last) in sales)
        {
            var sUser = new User { Email = $"{first.ToLower()}.{last.ToLower()}@wetacoo.com", Role = "Sales" };
            Users.Add(sUser);
            Salesmen.Add(new Salesman { FirstName = first, LastName = last, IdentityId = sUser.Id, IsActive = true });
        }

        // ── Product Templates (catalogo) ──
        ProductTemplates.AddRange([
            new ProductTemplate { Name = "Deposito Base", Description = "Piano deposito mensile base fino a 10m\u00b3", BasePrice = 89.90m, ProductType = "recurring", AreaId = areaMilano.Id, IsActive = true },
            new ProductTemplate { Name = "Deposito Premium", Description = "Piano deposito mensile premium fino a 20m\u00b3", BasePrice = 129.90m, ProductType = "recurring", AreaId = areaMilano.Id, IsActive = true },
            new ProductTemplate { Name = "Deposito XL", Description = "Piano deposito mensile XL fino a 40m\u00b3", BasePrice = 199.90m, ProductType = "recurring", AreaId = areaMilano.Id, IsActive = true },
            new ProductTemplate { Name = "Ritiro Standard", Description = "Servizio ritiro con 2 operatori", BasePrice = 149.00m, ProductType = "oneoff", IsActive = true },
            new ProductTemplate { Name = "Consegna Standard", Description = "Servizio consegna con 2 operatori", BasePrice = 149.00m, ProductType = "oneoff", IsActive = true },
            new ProductTemplate { Name = "Smaltimento", Description = "Conferimento a centro smaltimento", BasePrice = 79.00m, ProductType = "oneoff", IsActive = true },
            new ProductTemplate { Name = "Supplemento Inter-Area", Description = "Supplemento trasporto tra aree diverse", BasePrice = 120.00m, ProductType = "oneoff", IsActive = true },
            new ProductTemplate { Name = "Imballaggio Professionale", Description = "Servizio imballaggio materiali delicati", BasePrice = 35.00m, ProductType = "oneoff", IsActive = true },
        ]);

        // ── Question Templates (domande standard per questionari) ──
        QuestionTemplates.AddRange([
            new QuestionTemplate { Question = "Quanti metri cubi stimi di dover depositare?", QuestionType = "number", Visibility = "deposito", IsActive = true },
            new QuestionTemplate { Question = "Hai oggetti fragili o di valore?", QuestionType = "boolean", Visibility = "all", IsActive = true },
            new QuestionTemplate { Question = "Piano dell'abitazione (con o senza ascensore)?", QuestionType = "text", Visibility = "all", IsActive = true },
            new QuestionTemplate { Question = "Preferenza fascia oraria?", QuestionType = "select", Visibility = "all", IsActive = true, Notes = "Opzioni: mattina, pomeriggio, indifferente" },
            new QuestionTemplate { Question = "Note aggiuntive o richieste particolari?", QuestionType = "text", Visibility = "all", IsActive = true },
        ]);

        // ── Coupon ──
        Coupons.AddRange([
            new Coupon { Code = "WELCOME10", DiscountPercent = 10, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddMonths(3), IsActive = true },
            new Coupon { Code = "ESTATE2026", DiscountPercent = 15, ValidFrom = new DateTime(2026, 6, 1), ValidTo = new DateTime(2026, 8, 31), IsActive = true },
            new Coupon { Code = "FISSO20", DiscountFixed = 20.00m, ValidFrom = DateTime.Today, ValidTo = DateTime.Today.AddMonths(6), IsActive = true },
        ]);

        // ── ObjectTemplates (Shared Infrastructure — catalogo calcolatore volume) ──
        ObjectTemplates.AddRange([
            new ObjectTemplate { Name = "Armadio 2 ante", ObjectType = "mobile", Room = "Camera", DefaultVolume = 1.8m },
            new ObjectTemplate { Name = "Armadio 3 ante", ObjectType = "mobile", Room = "Camera", DefaultVolume = 2.4m },
            new ObjectTemplate { Name = "Letto matrimoniale", ObjectType = "mobile", Room = "Camera", DefaultVolume = 1.5m },
            new ObjectTemplate { Name = "Letto singolo", ObjectType = "mobile", Room = "Camera", DefaultVolume = 0.8m },
            new ObjectTemplate { Name = "Comodino", ObjectType = "mobile", Room = "Camera", DefaultVolume = 0.2m },
            new ObjectTemplate { Name = "Scrivania", ObjectType = "mobile", Room = "Studio", DefaultVolume = 0.8m },
            new ObjectTemplate { Name = "Libreria", ObjectType = "mobile", Room = "Studio", DefaultVolume = 1.2m },
            new ObjectTemplate { Name = "Divano 3 posti", ObjectType = "mobile", Room = "Soggiorno", DefaultVolume = 2.0m },
            new ObjectTemplate { Name = "Divano 2 posti", ObjectType = "mobile", Room = "Soggiorno", DefaultVolume = 1.4m },
            new ObjectTemplate { Name = "Tavolo da pranzo", ObjectType = "mobile", Room = "Soggiorno", DefaultVolume = 1.0m },
            new ObjectTemplate { Name = "Sedia", ObjectType = "mobile", Room = "Soggiorno", DefaultVolume = 0.15m },
            new ObjectTemplate { Name = "TV", ObjectType = "oggetto", Room = "Soggiorno", DefaultVolume = 0.3m },
            new ObjectTemplate { Name = "Lavatrice", ObjectType = "oggetto", Room = "Bagno", DefaultVolume = 0.5m },
            new ObjectTemplate { Name = "Frigorifero", ObjectType = "oggetto", Room = "Cucina", DefaultVolume = 0.8m },
            new ObjectTemplate { Name = "Scatola standard", ObjectType = "oggetto", Room = null, DefaultVolume = 0.06m },
            new ObjectTemplate { Name = "Scatola grande", ObjectType = "oggetto", Room = null, DefaultVolume = 0.12m },
            new ObjectTemplate { Name = "Materasso matrimoniale", ObjectType = "oggetto", Room = "Camera", DefaultVolume = 0.5m },
            new ObjectTemplate { Name = "Specchio", ObjectType = "oggetto", Room = null, DefaultVolume = 0.15m },
        ]);

        // ── Slot (prossimi 14 giorni per Milano e Roma) ──
        for (int d = 3; d <= 14; d++)
        {
            var date = DateTime.Today.AddDays(d);
            if (date.DayOfWeek is DayOfWeek.Sunday) continue;

            Slots.Add(new Slot { Date = date, AreaId = areaMilano.Id, WarehouseId = whMilano.Id, MaxVolume = 50, MaxServices = 5 });
            Slots.Add(new Slot { Date = date, AreaId = areaMilano.Id, WarehouseId = whMilano.Id, MaxVolume = 40, MaxServices = 4 });

            if (d % 2 == 0)
                Slots.Add(new Slot { Date = date, AreaId = areaRoma.Id, WarehouseId = whRoma.Id, MaxVolume = 40, MaxServices = 4 });
        }

        // ── Lead pre-popolati ──
        var lead1 = new Lead { Personal = new Personal("Anna", "Verdi", "anna.verdi@email.com", "+39 333 1234567") };
        var lead2 = new Lead { Personal = new Personal("Marco", "Neri", "marco.neri@email.com", "+39 340 9876543") };
        var lead3 = new Lead { Personal = new Personal("Giulia", "Romano", "giulia.romano@email.com", "+39 328 5551234") };
        Leads.AddRange([lead1, lead2, lead3]);

        // Identity per i lead
        foreach (var lead in Leads)
        {
            var u = new User { Email = lead.Personal.Email, Role = "Customer" };
            Users.Add(u);
            lead.IdentityId = u.Id;
        }

        // Marketing per i lead
        foreach (var lead in Leads)
        {
            MarketingClients.Add(new MarketingClient { CommercialLeadId = lead.Id, FunnelStep = "LeadCreated" });
        }

        // ── Asset (non-veicolo) ──
        Assets.AddRange([
            new Asset { Name = "Transpallet MI-01", AssetType = "transpallet", AreaId = areaMilano.Id, Status = "Available" },
            new Asset { Name = "Carrello MI-02", AssetType = "carrello", AreaId = areaMilano.Id, Status = "Available" },
            new Asset { Name = "Transpallet RM-01", AssetType = "transpallet", AreaId = areaRoma.Id, Status = "Available" },
            new Asset { Name = "Gru leggera TO-01", AssetType = "gru", AreaId = areaTorino.Id, Status = "Available" },
        ]);
    }

    // ── Entity counts per BC ──
    public Dictionary<string, int> GetBCCounts() => new()
    {
        ["Commercial"] = Leads.Count + Deals.Count + ProductTemplates.Count + QuestionTemplates.Count + Questionnaires.Count + Coupons.Count + Salesmen.Count,
        ["SharedInfrastructure"] = ObjectTemplates.Count + Areas.Count + Emails.Count,
        ["Operational"] = WorkOrders.Count + Plannings.Count + Slots.Count + Warehouses.Count + Vehicles.Count + Operators.Count + Assets.Count,
        ["Execution"] = Shifts.Count + Objects.Count + WarehouseOperations.Count + Labels.Count + Pallets.Count + VehicleOperations.Count + ObjectGroups.Count,
        ["Financial"] = FinancialClients.Count + Payments.Count,
        ["Identity"] = Users.Count,
        ["Marketing"] = MarketingClients.Count,
        ["Happiness"] = HappinessClients.Count,
    };
}

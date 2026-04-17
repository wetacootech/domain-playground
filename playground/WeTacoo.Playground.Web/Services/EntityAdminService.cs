using System.Reflection;
using WeTacoo.Domain.Commercial;
using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Operational;
using WeTacoo.Domain.Operational.Entities;
using WeTacoo.Domain.Execution;
using WeTacoo.Domain.Execution.Entities;
using WeTacoo.Domain.Financial;
using WeTacoo.Domain.SharedInfrastructure;
using WOp = WeTacoo.Domain.Operational;
using WEx = WeTacoo.Domain.Execution;

namespace WeTacoo.Playground.Web.Services;

/// <summary>
/// Admin service for the playground: hard-delete with cascade + force-edit via reflection.
/// Bypasses all domain guards (private setters, state machine). Use only in playground/dev.
/// </summary>
public class EntityAdminService
{
    private readonly PlaygroundState _state;
    public EntityAdminService(PlaygroundState state) { _state = state; }

    // ═══════════════════════════════════════════════════════════
    // DELETE — top-level aggregates
    // ═══════════════════════════════════════════════════════════

    public void DeleteLead(string leadId)
    {
        var lead = _state.Leads.FirstOrDefault(l => l.Id == leadId);
        if (lead == null) return;

        foreach (var dealId in lead.DealIds.ToList()) DeleteDeal(dealId);
        _state.MarketingClients.RemoveAll(m => m.CommercialLeadId == leadId);
        _state.HappinessClients.RemoveAll(h => GetStringProp(h, "CommercialLeadId") == leadId);
        _state.FinancialClients.RemoveAll(f => GetStringProp(f, "CommercialLeadId") == leadId || GetStringProp(f, "LeadId") == leadId);
        _state.Objects.RemoveAll(o => o.LeadId == leadId);
        if (lead.IdentityId != null) _state.Users.RemoveAll(u => u.Id == lead.IdentityId);
        _state.Leads.Remove(lead);
        if (_state.SelectedLeadId == leadId) _state.SelectedLeadId = null;
    }

    public void DeleteDeal(string dealId)
    {
        var deal = _state.Deals.FirstOrDefault(d => d.Id == dealId);
        if (deal == null) return;

        foreach (var q in deal.Quotations.ToList()) DeleteQuotation(dealId, q.Id);

        _state.Objects.RemoveAll(o => o.DealId == dealId);
        _state.Payments.RemoveAll(p => p.DealId == dealId);

        var lead = _state.Leads.FirstOrDefault(l => l.DealIds.Contains(dealId));
        lead?.DealIds.Remove(dealId);
        _state.Deals.Remove(deal);
    }

    public void DeleteQuotation(string dealId, string quotationId)
    {
        var deal = _state.Deals.FirstOrDefault(d => d.Id == dealId);
        var q = deal?.Quotations.FirstOrDefault(x => x.Id == quotationId);
        if (deal == null || q == null) return;

        foreach (var svc in q.Services.ToList()) DeleteServiceBooked(dealId, quotationId, svc.Id);
        deal.Quotations.Remove(q);

        if (deal.ActivePlan?.QuotationId == quotationId)
        {
            typeof(Deal).GetProperty(nameof(Deal.ActivePlan))!.SetValue(deal, null);
        }
    }

    public void DeleteServiceBooked(string dealId, string quotationId, string serviceId)
    {
        var deal = _state.Deals.FirstOrDefault(d => d.Id == dealId);
        var q = deal?.Quotations.FirstOrDefault(x => x.Id == quotationId);
        var svc = q?.Services.FirstOrDefault(s => s.Id == serviceId);
        if (q == null || svc == null) return;

        if (svc.WorkOrderId != null) DeleteWorkOrder(svc.WorkOrderId);
        q.Services.Remove(svc);
    }

    public void DeleteProduct(string dealId, string quotationId, string productId)
    {
        var q = _state.Deals.FirstOrDefault(d => d.Id == dealId)?.Quotations.FirstOrDefault(x => x.Id == quotationId);
        q?.Products.RemoveAll(p => p.Id == productId);
    }

    public void DeleteDraftPlan(string dealId, string quotationId, string draftPlanId)
    {
        var q = _state.Deals.FirstOrDefault(d => d.Id == dealId)?.Quotations.FirstOrDefault(x => x.Id == quotationId);
        q?.DraftPlans.RemoveAll(p => p.Id == draftPlanId);
    }

    public void DeleteWorkOrder(string workOrderId)
    {
        var wo = _state.WorkOrders.FirstOrDefault(w => w.Id == workOrderId);
        if (wo == null) return;

        // Shifts che contengono ServiceEntry per questo WO
        var shiftsToDelete = _state.Shifts.Where(s => s.ServiceEntries.Any(se => se.ServiceId == workOrderId)).ToList();
        foreach (var s in shiftsToDelete) DeleteShift(s.Id);

        // Mission: rimuovi ServiceRef al WO; se la mission resta senza refs, eliminala
        foreach (var p in _state.Plannings)
            foreach (var m in p.Missions.ToList())
            {
                m.ServiceRefs.RemoveAll(r => r.ServiceId == workOrderId);
                if (m.ServiceRefs.Count == 0) p.Missions.Remove(m);
            }

        // Back-ref: pulisci WorkOrderId sui ServiceBooked
        foreach (var deal in _state.Deals)
            foreach (var q in deal.Quotations)
                foreach (var svc in q.Services.Where(s => s.WorkOrderId == workOrderId))
                    svc.WorkOrderId = null;

        _state.WorkOrders.Remove(wo);
    }

    public void DeletePlanning(string planningId)
    {
        var p = _state.Plannings.FirstOrDefault(x => x.Id == planningId);
        if (p == null) return;
        foreach (var m in p.Missions.ToList()) DeleteMission(planningId, m.Id);
        _state.Plannings.Remove(p);
    }

    public void DeleteMission(string planningId, string missionId)
    {
        var p = _state.Plannings.FirstOrDefault(x => x.Id == planningId);
        var m = p?.Missions.FirstOrDefault(x => x.Id == missionId);
        if (p == null || m == null) return;

        _state.Shifts.RemoveAll(s => s.MissionId == missionId);
        p.Missions.Remove(m);
    }

    public void DeletePlanningTeam(string planningId, string teamId)
    {
        var p = _state.Plannings.FirstOrDefault(x => x.Id == planningId);
        p?.Teams.RemoveAll(t => t.Id == teamId);
    }

    public void DeletePlanningResource(string planningId, string resourceId)
    {
        var p = _state.Plannings.FirstOrDefault(x => x.Id == planningId);
        p?.Resources.RemoveAll(r => r.Id == resourceId);
    }

    public void DeleteShift(string shiftId)
    {
        _state.Shifts.RemoveAll(s => s.Id == shiftId);
    }

    public void DeleteServiceEntry(string shiftId, string entryId)
    {
        var s = _state.Shifts.FirstOrDefault(x => x.Id == shiftId);
        s?.ServiceEntries.RemoveAll(e => e.Id == entryId);
        // i Task che puntano a quell'entry diventano orfani → li eliminiamo
        s?.Tasks.RemoveAll(t => t.ServiceEntryId == entryId);
    }

    public void DeleteTask(string shiftId, string taskId)
    {
        var s = _state.Shifts.FirstOrDefault(x => x.Id == shiftId);
        s?.Tasks.RemoveAll(t => t.Id == taskId);
    }

    public void DeletePhysicalObject(string objectId)
    {
        _state.Labels.RemoveAll(l => GetStringProp(l, "ObjectId") == objectId);
        _state.Objects.RemoveAll(o => o.Id == objectId);

        // rimuovi dall'elenco selezionato dei ServiceBooked
        foreach (var deal in _state.Deals)
            foreach (var q in deal.Quotations)
                foreach (var svc in q.Services)
                    svc.SelectedObjectIds.RemoveAll(id => id == objectId);

        // rimuovi da Task.ObjectIds / WarehouseOperation.ObjectIds
        foreach (var s in _state.Shifts)
            foreach (var t in s.Tasks) t.ObjectIds.RemoveAll(id => id == objectId);
        foreach (var w in _state.WarehouseOperations) w.ObjectIds.RemoveAll(id => id == objectId);
    }

    public void DeleteWarehouse(string warehouseId)
    {
        // oggetti fisicamente presenti: hard delete (playground)
        var objsHere = _state.Objects.Where(o => o.Position?.WarehouseId == warehouseId).Select(o => o.Id).ToList();
        foreach (var oid in objsHere) DeletePhysicalObject(oid);

        _state.Slots.RemoveAll(s => s.WarehouseId == warehouseId);
        _state.WarehouseOperations.RemoveAll(w => w.WarehouseId == warehouseId);

        foreach (var op in _state.Operators)
            op.AssignedWarehouseIds.RemoveAll(id => id == warehouseId);

        _state.Warehouses.RemoveAll(w => w.Id == warehouseId);
    }

    public void DeleteVehicle(string vehicleId)
    {
        foreach (var p in _state.Plannings)
        {
            foreach (var m in p.Missions) m.VehicleResourceIds.RemoveAll(id => id == vehicleId);
            p.Resources.RemoveAll(r => r.SourceId == vehicleId && r.ResourceType == "vehicle");
        }
        _state.Vehicles.RemoveAll(v => v.Id == vehicleId);
    }

    public void DeleteOperator(string operatorId)
    {
        var op = _state.Operators.FirstOrDefault(o => o.Id == operatorId);
        if (op == null) return;

        foreach (var p in _state.Plannings)
        {
            foreach (var t in p.Teams) t.OperatorIds.RemoveAll(id => id == operatorId);
            p.Resources.RemoveAll(r => r.SourceId == operatorId && r.ResourceType == "operator");
        }
        if (op.IdentityId != null) _state.Users.RemoveAll(u => u.Id == op.IdentityId);
        _state.Operators.Remove(op);
    }

    public void DeletePayment(string paymentId) => _state.Payments.RemoveAll(p => p.Id == paymentId);
    public void DeleteCharge(string paymentId, string chargeId)
    {
        var p = _state.Payments.FirstOrDefault(x => x.Id == paymentId);
        p?.Charges.RemoveAll(c => c.Id == chargeId);
    }
    public void DeleteSimplifiedProduct(string paymentId, string productId)
    {
        var p = _state.Payments.FirstOrDefault(x => x.Id == paymentId);
        p?.Products.RemoveAll(x => x.Id == productId);
    }

    public void DeleteFinancialClient(string id)
    {
        _state.FinancialClients.RemoveAll(c => c.Id == id);
        foreach (var lead in _state.Leads) if (lead.FinancialClientId == id) lead.FinancialClientId = null;
    }

    public void DeleteUser(string userId)
    {
        _state.Users.RemoveAll(u => u.Id == userId);
        foreach (var lead in _state.Leads) if (lead.IdentityId == userId) lead.IdentityId = null;
        foreach (var op in _state.Operators) if (op.IdentityId == userId) op.IdentityId = null;
        foreach (var s in _state.Salesmen) if (s.IdentityId == userId) s.IdentityId = null;
    }

    public void DeleteSalesman(string id)
    {
        var s = _state.Salesmen.FirstOrDefault(x => x.Id == id);
        if (s == null) return;
        if (s.IdentityId != null) _state.Users.RemoveAll(u => u.Id == s.IdentityId);
        foreach (var d in _state.Deals) if (d.SalesmanId == id) d.SalesmanId = null;
        _state.Salesmen.Remove(s);
    }

    public void DeleteMarketingClient(string id) => _state.MarketingClients.RemoveAll(m => m.Id == id);
    public void DeleteHappinessClient(string id) => _state.HappinessClients.RemoveAll(h => h.Id == id);

    public void DeleteLabel(string id) => _state.Labels.RemoveAll(l => l.Id == id);

    public void DeleteAsset(string id)
    {
        foreach (var p in _state.Plannings)
        {
            foreach (var m in p.Missions) m.AssetResourceIds.RemoveAll(x => x == id);
            p.Resources.RemoveAll(r => r.SourceId == id && r.ResourceType == "asset");
        }
        _state.Assets.RemoveAll(a => a.Id == id);
    }

    public void DeletePallet(string id)
    {
        _state.Pallets.RemoveAll(p => p.Id == id);
        foreach (var o in _state.Objects) if (o.PalletId == id) o.PalletId = null;
    }

    public void DeleteVehicleOperation(string id) => _state.VehicleOperations.RemoveAll(v => v.Id == id);

    public void DeleteObjectGroup(string id)
    {
        _state.ObjectGroups.RemoveAll(g => g.Id == id);
        foreach (var o in _state.Objects) if (o.GroupId == id) o.GroupId = null;
    }

    public void DeleteEmail(string id) => _state.Emails.RemoveAll(e => e.Id == id);
    public void DeleteSlot(string id) => _state.Slots.RemoveAll(s => s.Id == id);
    public void DeleteCoupon(string id) => _state.Coupons.RemoveAll(c => c.Id == id);
    public void DeleteProductTemplate(string id) => _state.ProductTemplates.RemoveAll(p => p.Id == id);
    public void DeleteQuestionTemplate(string id) => _state.QuestionTemplates.RemoveAll(q => q.Id == id);
    public void DeleteObjectTemplate(string id) => _state.ObjectTemplates.RemoveAll(o => o.Id == id);
    public void DeleteWarehouseOperation(string id) => _state.WarehouseOperations.RemoveAll(w => w.Id == id);

    public void DeleteArea(string areaId)
    {
        // cascata pesante: magazzini, slot, veicoli, operatori in quell'area
        foreach (var wh in _state.Warehouses.Where(w => w.AreaId == areaId).Select(w => w.Id).ToList()) DeleteWarehouse(wh);
        foreach (var v in _state.Vehicles.Where(x => x.AreaId == areaId).Select(x => x.Id).ToList()) DeleteVehicle(v);
        foreach (var o in _state.Operators.Where(x => x.AreaId == areaId).Select(x => x.Id).ToList()) DeleteOperator(o);
        _state.Slots.RemoveAll(s => s.AreaId == areaId);
        _state.Areas.RemoveAll(a => a.Id == areaId);
        foreach (var d in _state.Deals) if (d.AreaId == areaId) d.AreaId = null;
        foreach (var pt in _state.ProductTemplates) if (pt.AreaId == areaId) pt.AreaId = null;
    }

    public void DeleteQuestionnaire(string id)
    {
        _state.Questionnaires.RemoveAll(q => q.Id == id);
        // Questionnaire unico per Quotation (review 2026-04-17): cascade su Quotation, non su SB.
        foreach (var deal in _state.Deals)
            foreach (var q in deal.Quotations.Where(qq => qq.QuestionnaireId == id))
                q.QuestionnaireId = null;
    }

    // ═══════════════════════════════════════════════════════════
    // DISPATCHER generico — usato dalla UI con (kind, id) o (kind, parentIds..., id)
    // ═══════════════════════════════════════════════════════════

    public void Delete(string kind, params string[] ids)
    {
        switch (kind)
        {
            case nameof(Lead): DeleteLead(ids[0]); break;
            case nameof(Deal): DeleteDeal(ids[0]); break;
            case nameof(Quotation): DeleteQuotation(ids[0], ids[1]); break;
            case nameof(ServiceBooked): DeleteServiceBooked(ids[0], ids[1], ids[2]); break;
            case nameof(Product): DeleteProduct(ids[0], ids[1], ids[2]); break;
            case nameof(DraftPlan): DeleteDraftPlan(ids[0], ids[1], ids[2]); break;
            case nameof(WorkOrder): DeleteWorkOrder(ids[0]); break;
            case nameof(Planning): DeletePlanning(ids[0]); break;
            case nameof(Mission): DeleteMission(ids[0], ids[1]); break;
            case nameof(PlanningTeam): DeletePlanningTeam(ids[0], ids[1]); break;
            case nameof(Resource): DeletePlanningResource(ids[0], ids[1]); break;
            case nameof(Shift): DeleteShift(ids[0]); break;
            case nameof(ServiceEntry): DeleteServiceEntry(ids[0], ids[1]); break;
            case "Task":
            case nameof(OperationalTask): DeleteTask(ids[0], ids[1]); break;
            case nameof(PhysicalObject): DeletePhysicalObject(ids[0]); break;
            case nameof(Warehouse): DeleteWarehouse(ids[0]); break;
            case nameof(Vehicle): DeleteVehicle(ids[0]); break;
            case nameof(Operator): DeleteOperator(ids[0]); break;
            case nameof(Payment): DeletePayment(ids[0]); break;
            case nameof(Charge): DeleteCharge(ids[0], ids[1]); break;
            case nameof(SimplifiedProduct): DeleteSimplifiedProduct(ids[0], ids[1]); break;
            case nameof(FinancialClient): DeleteFinancialClient(ids[0]); break;
            case "User":
            case "OperatorUser":
            case "AdminUser": DeleteUser(ids[0]); break;
            case nameof(Salesman): DeleteSalesman(ids[0]); break;
            case "MarketingClient": DeleteMarketingClient(ids[0]); break;
            case "HappinessClient": DeleteHappinessClient(ids[0]); break;
            case nameof(Label): DeleteLabel(ids[0]); break;
            case nameof(Slot): DeleteSlot(ids[0]); break;
            case nameof(Coupon): DeleteCoupon(ids[0]); break;
            case nameof(ProductTemplate): DeleteProductTemplate(ids[0]); break;
            case nameof(QuestionTemplate): DeleteQuestionTemplate(ids[0]); break;
            case nameof(Questionnaire): DeleteQuestionnaire(ids[0]); break;
            case "ObjectTemplate": DeleteObjectTemplate(ids[0]); break;
            case "Area": DeleteArea(ids[0]); break;
            case nameof(WarehouseOperation): DeleteWarehouseOperation(ids[0]); break;
            case "Asset": DeleteAsset(ids[0]); break;
            case "Pallet": DeletePallet(ids[0]); break;
            case "VehicleOperation": DeleteVehicleOperation(ids[0]); break;
            case "ObjectGroup": DeleteObjectGroup(ids[0]); break;
            case "Email": DeleteEmail(ids[0]); break;
            default: throw new InvalidOperationException($"Delete non supportato per {kind}");
        }
        _state.NotifyStateChanged();
    }

    // ═══════════════════════════════════════════════════════════
    // PREVIEW CASCADE — ritorna lista human-readable di cosa verrà eliminato
    // ═══════════════════════════════════════════════════════════

    public List<string> PreviewCascade(string kind, params string[] ids)
    {
        var r = new List<string>();
        switch (kind)
        {
            case nameof(Lead):
            {
                var lead = _state.Leads.FirstOrDefault(l => l.Id == ids[0]);
                if (lead == null) break;
                r.Add($"Lead {lead.Id} ({lead.Personal.FirstName} {lead.Personal.LastName})");
                foreach (var dealId in lead.DealIds) r.AddRange(PreviewCascade(nameof(Deal), dealId).Select(s => "  " + s));
                var mk = _state.MarketingClients.Count(m => m.CommercialLeadId == lead.Id);
                if (mk > 0) r.Add($"  {mk} MarketingClient");
                if (lead.IdentityId != null) r.Add($"  User {lead.IdentityId}");
                break;
            }
            case nameof(Deal):
            {
                var deal = _state.Deals.FirstOrDefault(d => d.Id == ids[0]);
                if (deal == null) break;
                r.Add($"Deal {deal.Id} ({deal.Status})");
                foreach (var q in deal.Quotations) r.AddRange(PreviewCascade(nameof(Quotation), deal.Id, q.Id).Select(s => "  " + s));
                var objs = _state.Objects.Count(o => o.DealId == deal.Id);
                if (objs > 0) r.Add($"  {objs} PhysicalObject");
                var pays = _state.Payments.Count(p => p.DealId == deal.Id);
                if (pays > 0) r.Add($"  {pays} Payment");
                break;
            }
            case nameof(Quotation):
            {
                var deal = _state.Deals.FirstOrDefault(d => d.Id == ids[0]);
                var q = deal?.Quotations.FirstOrDefault(x => x.Id == ids[1]);
                if (q == null) break;
                r.Add($"Quotation {q.Id} ({q.Status})");
                foreach (var s in q.Services) r.AddRange(PreviewCascade(nameof(ServiceBooked), ids[0], ids[1], s.Id).Select(x => "  " + x));
                break;
            }
            case nameof(ServiceBooked):
            {
                var q = _state.Deals.FirstOrDefault(d => d.Id == ids[0])?.Quotations.FirstOrDefault(x => x.Id == ids[1]);
                var svc = q?.Services.FirstOrDefault(s => s.Id == ids[2]);
                if (svc == null) break;
                r.Add($"ServiceBooked {svc.Id} ({svc.Type})");
                if (svc.WorkOrderId != null) r.AddRange(PreviewCascade(nameof(WorkOrder), svc.WorkOrderId).Select(s => "  " + s));
                break;
            }
            case nameof(WorkOrder):
            {
                var wo = _state.WorkOrders.FirstOrDefault(x => x.Id == ids[0]);
                if (wo == null) break;
                r.Add($"WorkOrder {wo.Id} ({wo.Status})");
                var shifts = _state.Shifts.Where(s => s.ServiceEntries.Any(se => se.ServiceId == wo.Id)).Count();
                if (shifts > 0) r.Add($"  {shifts} Shift");
                var missions = _state.Plannings.SelectMany(p => p.Missions).Count(m => m.ServiceRefs.Any(x => x.ServiceId == wo.Id));
                if (missions > 0) r.Add($"  ServiceRef rimosso da {missions} Mission");
                break;
            }
            case nameof(Planning):
            {
                var p = _state.Plannings.FirstOrDefault(x => x.Id == ids[0]);
                if (p == null) break;
                r.Add($"Planning {p.Id}");
                r.Add($"  {p.Missions.Count} Mission (+ Shift collegati)");
                r.Add($"  {p.Teams.Count} Team");
                r.Add($"  {p.Resources.Count} Resource");
                break;
            }
            case nameof(Mission):
            {
                var p = _state.Plannings.FirstOrDefault(x => x.Id == ids[0]);
                var m = p?.Missions.FirstOrDefault(x => x.Id == ids[1]);
                if (m == null) break;
                r.Add($"Mission {m.Id}");
                var sh = _state.Shifts.Count(s => s.MissionId == m.Id);
                if (sh > 0) r.Add($"  {sh} Shift");
                break;
            }
            case nameof(Shift):
            {
                var s = _state.Shifts.FirstOrDefault(x => x.Id == ids[0]);
                if (s == null) break;
                r.Add($"Shift {s.Id} ({s.Status})");
                if (s.ServiceEntries.Count > 0) r.Add($"  {s.ServiceEntries.Count} ServiceEntry");
                if (s.Tasks.Count > 0) r.Add($"  {s.Tasks.Count} Task");
                break;
            }
            case nameof(Warehouse):
            {
                var w = _state.Warehouses.FirstOrDefault(x => x.Id == ids[0]);
                if (w == null) break;
                r.Add($"Warehouse {w.Id} ({w.Name})");
                var objs = _state.Objects.Count(o => o.Position?.WarehouseId == w.Id);
                if (objs > 0) r.Add($"  {objs} PhysicalObject presenti");
                var slots = _state.Slots.Count(s => s.WarehouseId == w.Id);
                if (slots > 0) r.Add($"  {slots} Slot");
                break;
            }
            case nameof(Vehicle):
            {
                var v = _state.Vehicles.FirstOrDefault(x => x.Id == ids[0]);
                if (v == null) break;
                r.Add($"Vehicle {v.Id} ({v.Name})");
                var refs = _state.Plannings.SelectMany(p => p.Missions).Count(m => m.VehicleResourceIds.Contains(v.Id));
                if (refs > 0) r.Add($"  Rimosso da {refs} Mission");
                break;
            }
            case nameof(Operator):
            {
                var op = _state.Operators.FirstOrDefault(x => x.Id == ids[0]);
                if (op == null) break;
                r.Add($"Operator {op.Id} ({op.FullName})");
                if (op.IdentityId != null) r.Add($"  User {op.IdentityId}");
                break;
            }
            case nameof(Area):
            {
                var a = _state.Areas.FirstOrDefault(x => x.Id == ids[0]);
                if (a == null) break;
                r.Add($"Area {a.Id} ({a.Name})");
                r.Add($"  Tutti i Warehouse/Vehicle/Operator/Slot dell'area");
                break;
            }
            default:
                r.Add($"{kind} {string.Join("/", ids)}");
                break;
        }
        return r;
    }

    // ═══════════════════════════════════════════════════════════
    // FORCE EDIT — reflection-based, bypassa private setters e state machine
    // ═══════════════════════════════════════════════════════════

    public record VoSubField(string Name, Type Type, object? CurrentValue, bool IsEnum, bool IsIdRef, string? IdRefTargetType);

    public record EditableProperty(
        string Name,
        Type Type,
        object? CurrentValue,
        bool IsEnum,
        bool IsComplex,
        bool IsIdRef,
        string? IdRefTargetType,
        List<VoSubField>? VoSubFields,
        bool IsIdList = false,
        List<(string Id, string Label)>? IdListOptions = null);

    private static readonly HashSet<Type> _simpleTypes =
    [
        typeof(string), typeof(bool), typeof(int), typeof(long), typeof(decimal), typeof(double), typeof(float),
        typeof(DateTime), typeof(DateTime?), typeof(int?), typeof(decimal?), typeof(bool?), typeof(long?), typeof(double?), typeof(float?)
    ];

    public List<EditableProperty> GetEditableProperties(object entity)
    {
        if (entity == null) return [];
        var t = entity.GetType();
        var typeName = t.Name;
        var arNames = GetArTypeNames();

        // If whitelist present, use it (group VO sub-paths under their top-level prop)
        if (EditableFieldsWhitelist.HasPolicy(typeName))
        {
            var paths = EditableFieldsWhitelist.For(typeName);
            var topLevel = new List<string>();
            var voSubPaths = new Dictionary<string, List<string>>(); // voProp -> [subField,...]
            foreach (var path in paths)
            {
                var dot = path.IndexOf('.');
                if (dot < 0) topLevel.Add(path);
                else
                {
                    var voName = path[..dot];
                    var sub = path[(dot + 1)..];
                    if (!voSubPaths.ContainsKey(voName)) voSubPaths[voName] = new();
                    voSubPaths[voName].Add(sub);
                }
            }

            var result = new List<EditableProperty>();
            foreach (var name in topLevel)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                result.Add(MakeEditable(p, entity, arNames));
            }
            foreach (var (voName, subs) in voSubPaths)
            {
                var p = t.GetProperty(voName, BindingFlags.Public | BindingFlags.Instance);
                if (p == null) continue;
                var voInst = p.GetValue(entity);
                var voType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                var subFields = new List<VoSubField>();
                foreach (var subName in subs)
                {
                    var sp = voType.GetProperty(subName);
                    if (sp == null) continue;
                    var spt = Nullable.GetUnderlyingType(sp.PropertyType) ?? sp.PropertyType;
                    bool subIsIdRef = sp.PropertyType == typeof(string) && subName.EndsWith("Id") && subName != "Id";
                    string? subTarget = subIsIdRef ? MapIdStem(subName[..^2], arNames) : null;
                    var subVal = voInst != null ? sp.GetValue(voInst) : null;
                    subFields.Add(new VoSubField(subName, sp.PropertyType, subVal, spt.IsEnum, subIsIdRef, subTarget));
                }
                result.Add(new EditableProperty(voName, p.PropertyType, voInst, false, true, false, null, subFields));
            }
            return result;
        }

        // Fallback: default behavior (tutte le simple/enum/bool)
        var fallback = new List<EditableProperty>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (!p.CanRead) continue;
            var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            bool isSimple = _simpleTypes.Contains(p.PropertyType) || _simpleTypes.Contains(pt);
            bool isCollection = typeof(System.Collections.IEnumerable).IsAssignableFrom(pt) && pt != typeof(string);
            if (isCollection) continue;
            bool isEnum = pt.IsEnum;
            if (!isSimple && !isEnum) continue; // salta complex in fallback
            fallback.Add(MakeEditable(p, entity, arNames));
        }
        return fallback;
    }

    private EditableProperty MakeEditable(PropertyInfo p, object entity, HashSet<string> arNames)
    {
        var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        var val = p.GetValue(entity);
        bool isEnum = pt.IsEnum;
        bool isSimple = _simpleTypes.Contains(p.PropertyType) || _simpleTypes.Contains(pt);
        bool isComplex = !isSimple && !isEnum;
        bool isIdRef = p.PropertyType == typeof(string) && p.Name.EndsWith("Id") && p.Name != "Id";
        string? target = isIdRef ? MapIdStem(p.Name[..^2], arNames) : null;

        // List<string> di id (es. Mission.VehicleResourceIds, OperatorIds) — con opzioni contestuali
        bool isIdList = pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(List<>) && pt.GetGenericArguments()[0] == typeof(string);
        List<(string, string)>? listOptions = null;
        if (isIdList)
        {
            listOptions = GetIdListOptions(p.Name);
        }

        return new EditableProperty(p.Name, p.PropertyType, val, isEnum, isComplex, isIdRef, target, null, isIdList, listOptions);
    }

    private List<(string Id, string Label)> GetIdListOptions(string propertyName)
    {
        // Context-aware options per property naming convention
        return propertyName switch
        {
            "VehicleResourceIds" => _state.Plannings.SelectMany(p => p.Resources)
                .Where(r => r.ResourceType == "vehicle")
                .Select(r =>
                {
                    var v = _state.Vehicles.FirstOrDefault(x => x.Id == r.SourceId);
                    return (r.Id, v != null ? $"{v.Name} ({v.Plate})" : r.Id);
                }).ToList(),
            "AssetResourceIds" => _state.Plannings.SelectMany(p => p.Resources)
                .Where(r => r.ResourceType == "asset")
                .Select(r =>
                {
                    var a = _state.Assets.FirstOrDefault(x => x.Id == r.SourceId);
                    return (r.Id, a != null ? a.Name : r.Id);
                }).ToList(),
            "OperatorIds" => _state.Operators.Select(o => (o.Id, o.FullName)).ToList(),
            "AssignedWarehouseIds" => _state.Warehouses.Select(w => (w.Id, w.Name)).ToList(),
            _ => new List<(string, string)>()
        };
    }

    private HashSet<string> GetArTypeNames()
        => typeof(WeTacoo.Domain.Common.AggregateRoot).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.BaseType == typeof(WeTacoo.Domain.Common.AggregateRoot))
            .Select(t => t.Name).ToHashSet();

    private static string? MapIdStem(string stem, HashSet<string> arNames)
    {
        if (arNames.Contains(stem)) return stem;
        return stem switch
        {
            "Object" => arNames.Contains("PhysicalObject") ? "PhysicalObject" : null,
            "CommercialLead" => arNames.Contains("Lead") ? "Lead" : null,
            "Identity" => arNames.Contains("User") ? "User" : null,
            "Client" => arNames.Contains("FinancialClient") ? "FinancialClient" : null,
            "FinancialClient" => arNames.Contains("FinancialClient") ? "FinancialClient" : null,
            "WorkOrder" => arNames.Contains("WorkOrder") ? "WorkOrder" : null,
            "Team" => "PlanningTeam", // Entity nested in Planning
            _ => null
        };
    }

    /// <summary>Lista di istanze per popolare select dei cross-AR ref.</summary>
    public List<(string Id, string Label)> GetArInstancesForSelect(string arTypeName)
    {
        // Special case: PlanningTeam è Entity nested, scan across all plannings
        if (arTypeName == "PlanningTeam")
        {
            return _state.Plannings.SelectMany(p => p.Teams.Select(t => (t.Id,
                Label: string.Join(" + ", t.OperatorIds.Take(3).Select(oid =>
                    _state.Operators.FirstOrDefault(o => o.Id == oid)?.FullName ?? oid)))))
                .ToList();
        }

        var items = InstanceInspector.GetArCollections(_state)
            .FirstOrDefault(x => x.ArType.Name == arTypeName);
        if (items.Items == null) return new();
        var result = new List<(string, string)>();
        foreach (var it in items.Items)
        {
            if (it == null) continue;
            var id = it.GetType().GetProperty("Id")?.GetValue(it) as string ?? "";
            var label = InstanceInspector.BuildLabel(it);
            result.Add((id, label));
        }
        return result;
    }

    /// <summary>Ricostruisce un record VO sostituendo i sub-field indicati; supporta anche VO null (crea da zero con default).</summary>
    public void ForceUpdateVo(object entity, string voPropertyName, Dictionary<string, object?> subFieldValues)
    {
        var t = entity.GetType();
        var p = t.GetProperty(voPropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) throw new InvalidOperationException($"VO property {voPropertyName} non trovata su {t.Name}");
        var voType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        var ctor = voType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
            ?? throw new InvalidOperationException($"Nessun constructor pubblico trovato su {voType.Name}");
        var current = p.GetValue(entity);

        var args = ctor.GetParameters().Select(param =>
        {
            if (subFieldValues.TryGetValue(param.Name!, out var raw))
                return ConvertValueForParam(raw, param.ParameterType);
            if (current != null)
                return voType.GetProperty(param.Name!)?.GetValue(current);
            return param.HasDefaultValue ? param.DefaultValue : DefaultOf(param.ParameterType);
        }).ToArray();

        var newVo = ctor.Invoke(args);
        if (p.CanWrite) p.SetValue(entity, newVo);
        else
        {
            var backing = t.GetField($"<{voPropertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            backing?.SetValue(entity, newVo);
        }
        _state.NotifyStateChanged();
    }

    private static object? DefaultOf(Type t)
    {
        if (t == typeof(string)) return "";
        if (t.IsValueType) return Activator.CreateInstance(t);
        return null;
    }

    private static object? ConvertValueForParam(object? raw, Type targetType)
    {
        if (raw == null) return Nullable.GetUnderlyingType(targetType) != null || !targetType.IsValueType ? null : DefaultOf(targetType);
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (raw.GetType() == t) return raw;
        if (raw is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Nullable.GetUnderlyingType(targetType) != null ? null : DefaultOf(targetType);
            if (t.IsEnum) return Enum.Parse(t, s);
            return Convert.ChangeType(s, t, System.Globalization.CultureInfo.InvariantCulture);
        }
        return Convert.ChangeType(raw, t, System.Globalization.CultureInfo.InvariantCulture);
    }

    public void ForceUpdate(object entity, string propertyName, object? value)
    {
        var t = entity.GetType();
        var p = t.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (p == null) throw new InvalidOperationException($"Proprietà {propertyName} non trovata su {t.Name}");
        var targetType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        object? converted = value;

        // List<string> di ids (es. VehicleResourceIds)
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>) && targetType.GetGenericArguments()[0] == typeof(string))
        {
            var newList = new List<string>();
            if (value is IEnumerable<string> ids) newList.AddRange(ids.Where(x => !string.IsNullOrWhiteSpace(x)));
            else if (value is string csv && !string.IsNullOrWhiteSpace(csv))
                newList.AddRange(csv.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)));
            converted = newList;
        }
        else if (value is string s && targetType != typeof(string))
        {
            if (string.IsNullOrWhiteSpace(s) && Nullable.GetUnderlyingType(p.PropertyType) != null)
                converted = null;
            else if (targetType.IsEnum)
                converted = Enum.Parse(targetType, s);
            else
                converted = Convert.ChangeType(s, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (p.CanWrite)
        {
            p.SetValue(entity, converted);
        }
        else
        {
            // fallback: backing field
            var backing = t.GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (backing != null) backing.SetValue(entity, converted);
            else throw new InvalidOperationException($"Proprietà {propertyName} non scrivibile");
        }
        _state.NotifyStateChanged();
    }

    // ═══════════════════════════════════════════════════════════
    // Lookup helper per la UI — trova entità + ids "parent" dal solo Id
    // ═══════════════════════════════════════════════════════════

    public (object? entity, string kind, string[] ids) FindById(string id)
    {
        if (_state.Leads.FirstOrDefault(l => l.Id == id) is { } l) return (l, nameof(Lead), [id]);
        if (_state.Deals.FirstOrDefault(d => d.Id == id) is { } d) return (d, nameof(Deal), [id]);
        if (_state.WorkOrders.FirstOrDefault(w => w.Id == id) is { } w) return (w, nameof(WorkOrder), [id]);
        if (_state.Plannings.FirstOrDefault(p => p.Id == id) is { } p) return (p, nameof(Planning), [id]);
        if (_state.Shifts.FirstOrDefault(s => s.Id == id) is { } sh) return (sh, nameof(Shift), [id]);
        if (_state.Objects.FirstOrDefault(o => o.Id == id) is { } o) return (o, nameof(PhysicalObject), [id]);
        if (_state.Warehouses.FirstOrDefault(x => x.Id == id) is { } wh) return (wh, nameof(Warehouse), [id]);
        if (_state.Vehicles.FirstOrDefault(x => x.Id == id) is { } v) return (v, nameof(Vehicle), [id]);
        if (_state.Operators.FirstOrDefault(x => x.Id == id) is { } op) return (op, nameof(Operator), [id]);
        if (_state.Payments.FirstOrDefault(x => x.Id == id) is { } pay) return (pay, nameof(Payment), [id]);
        if (_state.Users.FirstOrDefault(x => x.Id == id) is { } u) return (u, "User", [id]);
        if (_state.Salesmen.FirstOrDefault(x => x.Id == id) is { } sa) return (sa, nameof(Salesman), [id]);
        if (_state.Labels.FirstOrDefault(x => x.Id == id) is { } lb) return (lb, nameof(Label), [id]);
        if (_state.Slots.FirstOrDefault(x => x.Id == id) is { } sl) return (sl, nameof(Slot), [id]);
        if (_state.Coupons.FirstOrDefault(x => x.Id == id) is { } c) return (c, nameof(Coupon), [id]);
        if (_state.ProductTemplates.FirstOrDefault(x => x.Id == id) is { } pt) return (pt, nameof(ProductTemplate), [id]);
        if (_state.QuestionTemplates.FirstOrDefault(x => x.Id == id) is { } qt) return (qt, nameof(QuestionTemplate), [id]);
        if (_state.Questionnaires.FirstOrDefault(x => x.Id == id) is { } qn) return (qn, nameof(Questionnaire), [id]);
        if (_state.ObjectTemplates.FirstOrDefault(x => x.Id == id) is { } ot) return (ot, "ObjectTemplate", [id]);
        if (_state.Areas.FirstOrDefault(x => x.Id == id) is { } ar) return (ar, "Area", [id]);
        if (_state.WarehouseOperations.FirstOrDefault(x => x.Id == id) is { } wop) return (wop, nameof(WarehouseOperation), [id]);
        if (_state.FinancialClients.FirstOrDefault(x => x.Id == id) is { } fc) return (fc, nameof(FinancialClient), [id]);
        if (_state.MarketingClients.FirstOrDefault(x => x.Id == id) is { } mc) return (mc, "MarketingClient", [id]);
        if (_state.HappinessClients.FirstOrDefault(x => x.Id == id) is { } hc) return (hc, "HappinessClient", [id]);
        if (_state.Assets.FirstOrDefault(x => x.Id == id) is { } ass) return (ass, "Asset", [id]);
        if (_state.Pallets.FirstOrDefault(x => x.Id == id) is { } pl) return (pl, "Pallet", [id]);
        if (_state.VehicleOperations.FirstOrDefault(x => x.Id == id) is { } vop) return (vop, "VehicleOperation", [id]);
        if (_state.ObjectGroups.FirstOrDefault(x => x.Id == id) is { } og) return (og, "ObjectGroup", [id]);
        if (_state.Emails.FirstOrDefault(x => x.Id == id) is { } em) return (em, "Email", [id]);
        return (null, "", []);
    }

    private static string? GetStringProp(object obj, string name)
    {
        var p = obj.GetType().GetProperty(name);
        return p?.GetValue(obj) as string;
    }

    // ═══════════════════════════════════════════════════════════
    // CREATE — nuove istanze con riferimenti (per Admin panel)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Ritorna le dipendenze richieste per creare una nuova istanza del kind.</summary>
    /// Ogni tuple e' (parameter name, AR target type per il picker).
    public static List<(string Name, string Target)> GetCreateRequirements(string kind) => kind switch
    {
        "Deal" => new() { ("LeadId", "Lead") },
        "Quotation" => new() { ("DealId", "Deal") },
        "ServiceBooked" => new() { ("DealId", "Deal"), ("QuotationId", "Quotation") },
        "Mission" => new() { ("PlanningId", "Planning") },
        "PlanningTeam" => new() { ("PlanningId", "Planning") },
        "Payment" => new() { ("DealId", "Deal") },
        "MarketingClient" => new() { ("CommercialLeadId", "Lead") },
        "HappinessClient" => new() { ("CommercialLeadId", "Lead") },
        "FinancialClient" => new() { ("CommercialLeadId", "Lead") },
        _ => new()
    };

    /// <summary>Crea un'istanza con default + riferimenti forniti.</summary>
    public object? CreateNew(string kind, Dictionary<string, string> refs)
    {
        object? created = null;
        switch (kind)
        {
            case "Lead":
                var lead = new Lead { Personal = new WeTacoo.Domain.Commercial.ValueObjects.Personal("Nuovo", "Cliente", "nuovo@example.com", "") };
                _state.Leads.Add(lead); created = lead; break;
            case "Deal":
                if (!refs.TryGetValue("LeadId", out var leadId)) return null;
                var deal = new Deal { LeadId = leadId };
                _state.Deals.Add(deal);
                var l = _state.Leads.FirstOrDefault(x => x.Id == leadId); l?.AddDeal(deal.Id);
                created = deal; break;
            case "Quotation":
                if (!refs.TryGetValue("DealId", out var qDealId)) return null;
                var qDeal = _state.Deals.FirstOrDefault(d => d.Id == qDealId); if (qDeal == null) return null;
                var q = new Quotation { DealId = qDealId, PaymentCondition = new WeTacoo.Domain.Commercial.ValueObjects.PaymentCondition(22m, 30) };
                qDeal.Quotations.Add(q); created = q; break;
            case "ServiceBooked":
                if (!refs.TryGetValue("DealId", out var sDealId) || !refs.TryGetValue("QuotationId", out var sQuotId)) return null;
                var sQuot = _state.Deals.FirstOrDefault(d => d.Id == sDealId)?.Quotations.FirstOrDefault(qq => qq.Id == sQuotId); if (sQuot == null) return null;
                var svc = new ServiceBooked { Type = WeTacoo.Domain.Commercial.Enums.ServiceBookedType.Ritiro };
                sQuot.Services.Add(svc); created = svc; break;
            case "Planning":
                var pl = new WOp.Planning { Date = DateTime.Today.AddDays(1) };
                _state.Plannings.Add(pl); created = pl; break;
            case "Mission":
                if (!refs.TryGetValue("PlanningId", out var mPlanId)) return null;
                var mPlanning = _state.Plannings.FirstOrDefault(p => p.Id == mPlanId); if (mPlanning == null) return null;
                var mission = new Mission();
                mPlanning.Missions.Add(mission); created = mission; break;
            case "PlanningTeam":
                if (!refs.TryGetValue("PlanningId", out var tPlanId)) return null;
                var tPlanning = _state.Plannings.FirstOrDefault(p => p.Id == tPlanId); if (tPlanning == null) return null;
                var team = new PlanningTeam();
                tPlanning.Teams.Add(team); created = team; break;
            case "WorkOrder":
                var wo = new WorkOrder {
                    Type = WeTacoo.Domain.Operational.Enums.WorkOrderType.Operational,
                    ServiceType = new WeTacoo.Domain.Operational.ValueObjects.ServiceTypeVO(
                        WeTacoo.Domain.Operational.Enums.ServiceTypeEnum.Ritiro, false, false, null)
                };
                _state.WorkOrders.Add(wo); created = wo; break;
            case "Shift":
                var shift = new Shift { Date = DateTime.Today };
                _state.Shifts.Add(shift); created = shift; break;
            case "Warehouse":
                var wh = new Warehouse { Name = "Nuovo Magazzino" };
                _state.Warehouses.Add(wh); created = wh; break;
            case "Vehicle":
                var v = new Vehicle { Name = "Nuovo Veicolo" };
                _state.Vehicles.Add(v); created = v; break;
            case "Asset":
                var a = new Asset { Name = "Nuovo Asset" };
                _state.Assets.Add(a); created = a; break;
            case "Operator":
                var op = new Operator { FirstName = "Nuovo", LastName = "Operatore" };
                _state.Operators.Add(op); created = op; break;
            case "Slot":
                var sl = new Slot { Date = DateTime.Today, MaxVolume = 50, MaxServices = 5 };
                _state.Slots.Add(sl); created = sl; break;
            case "Area":
                var ar = new WeTacoo.Domain.SharedInfrastructure.Area { Name = "Nuova Area", City = "Milano" };
                _state.Areas.Add(ar); created = ar; break;
            case "ObjectTemplate":
                var ot = new WeTacoo.Domain.SharedInfrastructure.ObjectTemplate { Name = "Nuovo Oggetto", DefaultVolume = 1m };
                _state.ObjectTemplates.Add(ot); created = ot; break;
            case "ProductTemplate":
                var pt = new ProductTemplate { Name = "Nuovo Prodotto", BasePrice = 10m, ProductType = "oneoff" };
                _state.ProductTemplates.Add(pt); created = pt; break;
            case "QuestionTemplate":
                var qt = new QuestionTemplate { Question = "Nuova domanda?", QuestionType = "text" };
                _state.QuestionTemplates.Add(qt); created = qt; break;
            case "Questionnaire":
                var qn = new Questionnaire();
                _state.Questionnaires.Add(qn); created = qn; break;
            case "Coupon":
                var cp = new Coupon { Code = "NEW", DiscountPercent = 10 };
                _state.Coupons.Add(cp); created = cp; break;
            case "Salesman":
                var sm = new Salesman { FirstName = "Nuovo", LastName = "Sales" };
                _state.Salesmen.Add(sm); created = sm; break;
            case "User":
                var u = new WeTacoo.Domain.Identity.User { Email = "new@example.com", Role = "Customer" };
                _state.Users.Add(u); created = u; break;
            case "Payment":
                if (!refs.TryGetValue("DealId", out var payDealId)) return null;
                var pay = new Payment { DealId = payDealId, PaymentType = "OneOff" };
                _state.Payments.Add(pay); created = pay; break;
            case "FinancialClient":
                if (!refs.TryGetValue("CommercialLeadId", out var fcLeadId)) return null;
                var fc = new FinancialClient { CommercialLeadId = fcLeadId };
                _state.FinancialClients.Add(fc); created = fc; break;
            case "MarketingClient":
                if (!refs.TryGetValue("CommercialLeadId", out var mkLeadId)) return null;
                var mk = new WeTacoo.Domain.Marketing.MarketingClient { CommercialLeadId = mkLeadId, FunnelStep = "LeadCreated" };
                _state.MarketingClients.Add(mk); created = mk; break;
            case "HappinessClient":
                if (!refs.TryGetValue("CommercialLeadId", out var hxLeadId)) return null;
                var hx = new WeTacoo.Domain.Happiness.HappinessClient { CommercialLeadId = hxLeadId };
                _state.HappinessClients.Add(hx); created = hx; break;
            case "PhysicalObject":
                var po = new PhysicalObject { Name = "Nuovo Oggetto", Volume = 1m };
                _state.Objects.Add(po); created = po; break;
            case "Pallet":
                var plt = new Pallet { Name = "Nuovo Pallet" };
                _state.Pallets.Add(plt); created = plt; break;
            case "ObjectGroup":
                var og = new ObjectGroup { Name = "Nuovo Gruppo" };
                _state.ObjectGroups.Add(og); created = og; break;
            case "Label":
                var lb = new Label { Code = "NEW-LABEL", City = "Milano" };
                _state.Labels.Add(lb); created = lb; break;
            case "VehicleOperation":
                var vop = new VehicleOperation { Type = "check in", Status = "Pending" };
                _state.VehicleOperations.Add(vop); created = vop; break;
            case "WarehouseOperation":
                var whop = new WarehouseOperation { OperationType = "IN", Status = "Pending" };
                _state.WarehouseOperations.Add(whop); created = whop; break;
            case "Email":
                var em = new WeTacoo.Domain.SharedInfrastructure.Email { To = "new@example.com", Subject = "Nuova email", Body = "", Type = "notification", Status = "pending" };
                _state.Emails.Add(em); created = em; break;
        }
        if (created != null) _state.NotifyStateChanged();
        return created;
    }

    public bool CanCreate(string kind) => kind switch
    {
        "Lead" or "Deal" or "Quotation" or "ServiceBooked" or "Planning" or "Mission" or "PlanningTeam" or
        "WorkOrder" or "Shift" or "Warehouse" or "Vehicle" or "Asset" or "Operator" or
        "Slot" or "Area" or "ObjectTemplate" or "ProductTemplate" or "QuestionTemplate" or "Questionnaire" or
        "Coupon" or "Salesman" or "User" or "Payment" or "FinancialClient" or "MarketingClient" or
        "HappinessClient" or "PhysicalObject" or "Pallet" or "ObjectGroup" or "Label" or
        "VehicleOperation" or "WarehouseOperation" or "Email" => true,
        _ => false
    };
}

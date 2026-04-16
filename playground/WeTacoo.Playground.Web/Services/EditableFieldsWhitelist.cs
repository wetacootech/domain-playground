namespace WeTacoo.Playground.Web.Services;

/// <summary>
/// Whitelist dei campi editabili per ciascun AR/Entity. Se un tipo non e' in whitelist,
/// l'editor applica il comportamento di default (tutte le prop simple/enum/bool).
/// I path con "." (es. "Personal.FirstName") sono sub-field di VO: la UI li renderizza
/// espansi e al submit ricostruisce il record VO via primary ctor.
/// </summary>
public static class EditableFieldsWhitelist
{
    public static readonly Dictionary<string, string[]> Paths = new()
    {
        ["Lead"] = new[] { "Personal.FirstName", "Personal.LastName", "Personal.Email", "Personal.Phone", "IdentityId" },
        ["Deal"] = new string[] { },
        ["Quotation"] = new string[] { },
        ["Product"] = new[] { "Name", "Description", "Price" },
        ["ServiceBooked"] = new[] {
            "Type", "ScheduledDate", "ScheduledSlot",
            "ServiceAddress.AreaId"
        },
        ["DraftPlan"] = new[] { "Description", "MonthlyFee", "EstimatedM3", "AreaId" },
        ["WorkOrder"] = new[] { "EstimatedVolume", "ScheduledDate", "ScheduledSlot" },
        ["Planning"] = new[] { "Date" },
        ["Mission"] = new[] { "TeamId", "VehicleResourceIds", "Notes" },
        ["PlanningTeam"] = new[] { "Notes" },
        ["Resource"] = new[] { "ResourceType", "SourceId", "AreaId", "AvailabilitySlot", "Notes" },
        ["Shift"] = new[] { "MissionId", "Date" },
        ["ServiceEntry"] = new[] { "Type" },
        ["OperationalTask"] = new[] { "Type", "StartTime", "EndTime", "IsExtra", "Notes" },
        ["PhysicalObject"] = new[] { "Name", "Volume", "LabelId", "GroupId", "PalletId", "LeadId", "DealId" },
        ["Pallet"] = new[] { "LabelId", "Description" },
        ["Label"] = new[] { "Code", "City", "IsReferral" },
        ["Warehouse"] = new[] { "Name", "Address", "AreaId", "Capacity" },
        ["Vehicle"] = new[] { "Name", "Plate", "AreaId", "Capacity" },
        ["Operator"] = new[] { "FirstName", "LastName", "AreaId", "IdentityId" },
        ["Slot"] = new[] { "Date", "AreaId", "WarehouseId", "MaxVolume", "MaxServices", "TimeStart", "TimeEnd" },
        ["Inspection"] = new[] { "Caratteristiche", "DataRichiesta" },
        ["Area"] = new[] { "Name", "City", "MinBookingDays", "MinDeliveryDays" },
        ["ObjectTemplate"] = new[] { "Name", "ObjectType", "Room", "DefaultVolume" },
        ["ProductTemplate"] = new[] { "Name", "Description", "BasePrice", "ProductType", "AreaId", "IsActive" },
        ["QuestionTemplate"] = new[] { "Question", "QuestionType", "Visibility", "IsActive", "Notes" },
        ["Questionnaire"] = new[] { "Notes", "IsVerified" },
        ["Question"] = new[] { "QuestionTemplateId" },
        ["Coupon"] = new[] { "Code", "DiscountPercent", "DiscountFixed", "ValidFrom", "ValidTo", "IsActive" },
        ["Salesman"] = new[] { "FirstName", "LastName", "IsActive" },
        ["Payment"] = new[] { "PaymentType" },
        ["Charge"] = new[] { "Amount", "DueDate", "Notes" },
        ["SimplifiedProduct"] = new[] { "Name", "Description", "Price" },
        ["FinancialClient"] = new[] { "BillingName", "BillingAddress", "VatNumber", "Notes" },
        ["User"] = new[] { "Email", "Role", "IsActive" },
        ["AdminUser"] = new[] { "Email", "IsActive" },
        ["OperatorUser"] = new[] { "Email", "IsActive" },
        ["MarketingClient"] = new[] { "FunnelStep" },
        ["HappinessClient"] = new string[] { },
        ["Asset"] = new[] { "Name", "AssetType", "AreaId", "Status" },
        ["VehicleOperation"] = new[] { "VehicleId", "Type", "StartTime", "EndTime", "Status" },
        ["ObjectGroup"] = new[] { "Name", "Description" },
        ["Email"] = new[] { "To", "Subject", "Body", "Type", "Status" },
        ["WarehouseOperation"] = new[] { "OperationType", "StartTime", "EndTime", "Status" },
        ["MarketingDeal"] = new[] { "FunnelQuotationStep" },
        ["HappinessService"] = new[] { "SatisfactionScore", "Feedback", "SurveyDate" },
    };

    public static bool HasPolicy(string typeName) => Paths.ContainsKey(typeName);
    public static string[] For(string typeName) => Paths.TryGetValue(typeName, out var v) ? v : Array.Empty<string>();
}

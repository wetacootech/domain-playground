namespace WeTacoo.Domain.Operational.ValueObjects;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Operational.Enums;

public record ServiceTypeVO(ServiceTypeEnum Type, bool IsPartial, bool IsAutonomous, string? AreaId) : ValueObject;

/// <summary>
/// Snapshot commerciale al momento della creazione del WorkOrder (DDD5 §2.2e, review 2026-04-16).
/// MovingIds: lista di altri ServiceBooked ID appaiati in una operazione di trasloco.
/// HasPlan: indica se il servizio e' associato a un piano ricorrente attivo.
/// </summary>
public record CommercialData(string LeadId, string? QuestionnaireId, string? Notes, List<string> MovingIds, bool HasPlan) : ValueObject;
public record OperationalDataVO(string? InspectionId, string ServiceSubType, string? Notes) : ValueObject;

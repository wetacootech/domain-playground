namespace WeTacoo.Domain.Operational.ValueObjects;
using WeTacoo.Domain.Common;
using WeTacoo.Domain.Operational.Enums;

public record ServiceTypeVO(ServiceTypeEnum Type, bool IsPartial, bool IsAutonomous, bool IsTrasloco, string? AreaId) : ValueObject;
public record CommercialData(string LeadId, string? QuestionnaireId, string? Notes) : ValueObject;
public record OperationalDataVO(string? InspectionId, string ServiceSubType, string? Notes) : ValueObject;

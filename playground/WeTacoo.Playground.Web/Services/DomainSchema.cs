namespace WeTacoo.Playground.Web.Services;

public enum SchemaKind
{
    Primitive,
    Enum,
    ValueObject,
    InnerEntity,
    InnerEntityList,
    CrossARRef,
    CrossARRefList,
    PrimitiveList,
    ValueObjectList
}

public record SchemaProperty(
    string Name,
    string TypeName,
    SchemaKind Kind,
    string? RelatedTypeName,
    bool IsNullable,
    bool IsComputed);

public record SchemaType(
    string Name,
    string Bc,
    bool IsAR,
    List<SchemaProperty> Properties,
    string? SummaryDoc);

public static class DomainSchemaInspector
{
    // La pagina /bc/schema mostra lo schema del dominio TARGET (diagramma TO BE DDD 7),
    // non quello del codice C# semplificato. La definizione è curata in CuratedDomainSchema.

    public static List<SchemaType> GetAll() => CuratedDomainSchema.GetAll();

    public static List<string> GetBcOrder() => new()
    {
        "Commercial", "Operational", "Execution", "Financial",
        "Identity", "Marketing", "Happiness", "SharedInfrastructure"
    };

    public static SchemaType? FindEntity(string name) => CuratedDomainSchema.FindEntity(name);

    public static SchemaType? FindAR(string name) => CuratedDomainSchema.FindAR(name);
}

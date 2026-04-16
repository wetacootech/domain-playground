namespace WeTacoo.Playground.Web.Services;

using System.Reflection;
using WeTacoo.Domain.Common;

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
    private static List<SchemaType>? _cache;
    private static readonly Type ARBase = typeof(AggregateRoot);
    private static readonly Type EntityBase = typeof(Entity);
    private static readonly Type VOBase = typeof(ValueObject);

    public static List<SchemaType> GetAll()
    {
        if (_cache != null) return _cache;

        var asm = ARBase.Assembly;
        var allTypes = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .ToList();

        // Only direct descendants of AggregateRoot or Entity (skip further subclasses like AdminUser)
        var schemaTypes = allTypes
            .Where(t => t.BaseType == ARBase || t.BaseType == EntityBase)
            .ToList();

        var arNames = schemaTypes.Where(t => t.IsSubclassOf(ARBase)).Select(t => t.Name).ToHashSet();

        var result = new List<SchemaType>();
        foreach (var t in schemaTypes)
        {
            var bc = ExtractBc(t.Namespace ?? "");
            var isAR = t.IsSubclassOf(ARBase);

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.DeclaringType == t) // only declared on this type
                .Where(p => p.Name != "Id") // metadata
                .Select(p => Classify(p, arNames))
                .ToList();

            result.Add(new SchemaType(t.Name, bc, isAR, props, null));
        }

        _cache = result;
        return result;
    }

    public static List<string> GetBcOrder() => new()
    {
        "Commercial", "Operational", "Execution", "Financial",
        "Identity", "Marketing", "Happiness", "SharedInfrastructure"
    };

    public static SchemaType? FindEntity(string name)
        => GetAll().FirstOrDefault(s => !s.IsAR && s.Name == name);

    public static SchemaType? FindAR(string name)
        => GetAll().FirstOrDefault(s => s.IsAR && s.Name == name);

    private static string ExtractBc(string ns)
    {
        var parts = ns.Split('.');
        var idx = Array.IndexOf(parts, "Domain");
        return idx >= 0 && parts.Length > idx + 1 ? parts[idx + 1] : ns;
    }

    private static SchemaProperty Classify(PropertyInfo p, HashSet<string> arNames)
    {
        var rawType = p.PropertyType;
        var underlying = Nullable.GetUnderlyingType(rawType);
        bool isNullable = underlying != null || !rawType.IsValueType;
        var t = underlying ?? rawType;
        bool computed = !p.CanWrite;

        // List<X>
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var inner = t.GetGenericArguments()[0];
            if (inner.IsSubclassOf(EntityBase))
                return new(p.Name, $"List<{inner.Name}>", SchemaKind.InnerEntityList, inner.Name, false, computed);
            if (IsValueObject(inner))
                return new(p.Name, $"List<{inner.Name}>", SchemaKind.ValueObjectList, inner.Name, false, computed);
            if (inner == typeof(string) && p.Name.EndsWith("Ids"))
            {
                var stem = p.Name[..^3];
                var target = MapIdStem(stem, arNames);
                return new(p.Name, "List<string>", SchemaKind.CrossARRefList, target, false, computed);
            }
            return new(p.Name, $"List<{inner.Name}>", SchemaKind.PrimitiveList, inner.Name, false, computed);
        }

        // Inner Entity (rare as direct type, most are via Id)
        if (t.IsSubclassOf(EntityBase))
            return new(p.Name, t.Name, SchemaKind.InnerEntity, t.Name, isNullable, computed);

        // Value Object
        if (IsValueObject(t))
            return new(p.Name, t.Name, SchemaKind.ValueObject, t.Name, isNullable, computed);

        // Enum
        if (t.IsEnum)
            return new(p.Name, t.Name, SchemaKind.Enum, t.Name, isNullable, computed);

        // Cross-AR reference via naming
        if (t == typeof(string) && p.Name.EndsWith("Id") && p.Name != "Id")
        {
            var stem = p.Name[..^2];
            var target = MapIdStem(stem, arNames);
            if (target != null)
                return new(p.Name, "string", SchemaKind.CrossARRef, target, isNullable, computed);
        }

        return new(p.Name, FormatType(rawType), SchemaKind.Primitive, null, isNullable, computed);
    }

    private static bool IsValueObject(Type t)
    {
        // ValueObject is an abstract record; records inherit via base chain
        var b = t.BaseType;
        while (b != null)
        {
            if (b == VOBase) return true;
            b = b.BaseType;
        }
        return false;
    }

    private static string? MapIdStem(string stem, HashSet<string> arNames)
    {
        if (arNames.Contains(stem)) return stem;
        return stem switch
        {
            "Object" => arNames.Contains("PhysicalObject") ? "PhysicalObject" : null,
            "CommercialLead" => arNames.Contains("Lead") ? "Lead" : null,
            "Source" => null,
            "Stripe" => null,
            "Hubspot" => null,
            "Identity" => arNames.Contains("User") ? "User" : null,
            "FinancialClient" => arNames.Contains("FinancialClient") ? "FinancialClient" : null,
            "Client" => arNames.Contains("FinancialClient") ? "FinancialClient" : null,
            "Contact" => null,
            "Asset" => null,
            _ => null
        };
    }

    private static string FormatType(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is Type n) return n.Name + "?";
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(DateTime)) return "DateTime";
        if (t == typeof(double)) return "double";
        return t.Name;
    }
}

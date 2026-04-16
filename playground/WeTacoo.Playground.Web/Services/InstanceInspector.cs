namespace WeTacoo.Playground.Web.Services;

using System.Collections;
using System.Reflection;
using WeTacoo.Domain.Common;

public record InstanceProperty(string Name, SchemaKind Kind, string? RelatedTypeName, object? Value);

public record InstanceView(object Raw, string Type, string Id, string Label, List<InstanceProperty> Properties);

public static class InstanceInspector
{
    private static readonly Type ARBase = typeof(AggregateRoot);
    private static readonly Type EntityBase = typeof(Entity);
    private static readonly Type VOBase = typeof(ValueObject);

    /// <summary>Finds all List&lt;T&gt; collections on PlaygroundState where T inherits AggregateRoot.</summary>
    public static List<(Type ArType, IList Items)> GetArCollections(object state)
    {
        var result = new List<(Type, IList)>();
        foreach (var p in state.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var t = p.PropertyType;
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(List<>)) continue;
            var inner = t.GetGenericArguments()[0];
            if (!inner.IsSubclassOf(ARBase)) continue;
            if (p.GetValue(state) is IList list) result.Add((inner, list));
        }
        return result;
    }

    /// <summary>Reads all instance properties classified for rendering.</summary>
    public static InstanceView Inspect(object instance)
    {
        var t = instance.GetType();
        var arNames = GetArTypeNames();
        var props = new List<InstanceProperty>();

        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.DeclaringType != t) continue;
            if (p.Name == "Id") continue;
            var value = SafeGet(p, instance);
            props.Add(ClassifyInstance(p, value, arNames));
        }

        var id = (t.GetProperty("Id")?.GetValue(instance) as string) ?? "?";
        return new InstanceView(instance, t.Name, id, BuildLabel(instance, t), props);
    }

    /// <summary>Build a human-readable label for an AR/Entity instance.</summary>
    public static string BuildLabel(object instance, Type? type = null)
    {
        type ??= instance.GetType();
        // Convention probes
        var personal = type.GetProperty("Personal")?.GetValue(instance);
        if (personal != null)
        {
            var fn = personal.GetType().GetProperty("FirstName")?.GetValue(personal) as string;
            var ln = personal.GetType().GetProperty("LastName")?.GetValue(personal) as string;
            if (!string.IsNullOrWhiteSpace(fn) || !string.IsNullOrWhiteSpace(ln)) return $"{fn} {ln}".Trim();
        }
        var name = type.GetProperty("Name")?.GetValue(instance) as string;
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var email = type.GetProperty("Email")?.GetValue(instance) as string;
        if (!string.IsNullOrWhiteSpace(email)) return email;
        var code = type.GetProperty("Code")?.GetValue(instance) as string;
        if (!string.IsNullOrWhiteSpace(code)) return code;
        var status = type.GetProperty("Status")?.GetValue(instance);
        if (status != null) return $"{type.Name} [{status}]";
        return type.Name;
    }

    /// <summary>Scalar rows suitable for rendering inline inside a node block (primitive + enum).</summary>
    public static List<(string Name, string Value, bool Computed)> GetScalarRows(object instance)
    {
        var view = Inspect(instance);
        var rows = new List<(string, string, bool)>();
        foreach (var p in view.Properties)
        {
            if (p.Kind != SchemaKind.Primitive && p.Kind != SchemaKind.Enum) continue;
            if (p.Value is IList) continue; // primitive list — skip in summary
            var v = FormatValue(p.Value);
            if (v.Length > 40) v = v[..38] + "…";
            rows.Add((p.Name, v, false));
        }
        return rows;
    }

    public static string FormatValue(object? v)
    {
        if (v is null) return "—";
        if (v is string s) return string.IsNullOrEmpty(s) ? "(vuoto)" : s;
        if (v is bool b) return b ? "true" : "false";
        if (v is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
        if (v is decimal d) return d.ToString("0.##");
        if (v is Enum e) return e.ToString();
        if (v is IList list && v is not string)
        {
            if (list.Count == 0) return "[]";
            var parts = new List<string>();
            foreach (var item in list) parts.Add(FormatValue(item));
            return "[" + string.Join(", ", parts) + "]";
        }
        return v.ToString() ?? "—";
    }

    private static HashSet<string> GetArTypeNames()
    {
        return ARBase.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.BaseType == ARBase)
            .Select(t => t.Name)
            .ToHashSet();
    }

    private static InstanceProperty ClassifyInstance(PropertyInfo p, object? value, HashSet<string> arNames)
    {
        var rawType = p.PropertyType;
        var underlying = Nullable.GetUnderlyingType(rawType);
        var t = underlying ?? rawType;

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var inner = t.GetGenericArguments()[0];
            if (inner.IsSubclassOf(EntityBase))
                return new(p.Name, SchemaKind.InnerEntityList, inner.Name, value);
            if (IsVo(inner))
                return new(p.Name, SchemaKind.ValueObjectList, inner.Name, value);
            if (inner == typeof(string) && p.Name.EndsWith("Ids"))
            {
                var stem = p.Name[..^3];
                var target = MapStem(stem, arNames);
                return new(p.Name, SchemaKind.CrossARRefList, target, value);
            }
            return new(p.Name, SchemaKind.PrimitiveList, inner.Name, value);
        }

        if (t.IsSubclassOf(EntityBase))
            return new(p.Name, SchemaKind.InnerEntity, t.Name, value);

        if (IsVo(t))
            return new(p.Name, SchemaKind.ValueObject, t.Name, value);

        if (t.IsEnum)
            return new(p.Name, SchemaKind.Enum, t.Name, value);

        if (t == typeof(string) && p.Name.EndsWith("Id") && p.Name != "Id")
        {
            var stem = p.Name[..^2];
            var target = MapStem(stem, arNames);
            if (target != null)
                return new(p.Name, SchemaKind.CrossARRef, target, value);
        }

        return new(p.Name, SchemaKind.Primitive, null, value);
    }

    private static bool IsVo(Type t)
    {
        var b = t.BaseType;
        while (b != null) { if (b == VOBase) return true; b = b.BaseType; }
        return false;
    }

    private static string? MapStem(string stem, HashSet<string> ars)
    {
        if (ars.Contains(stem)) return stem;
        return stem switch
        {
            "Object" => ars.Contains("PhysicalObject") ? "PhysicalObject" : null,
            "CommercialLead" => ars.Contains("Lead") ? "Lead" : null,
            "Identity" => ars.Contains("User") ? "User" : null,
            "Client" => ars.Contains("FinancialClient") ? "FinancialClient" : null,
            _ => null
        };
    }

    private static object? SafeGet(PropertyInfo p, object instance)
    {
        try { return p.GetValue(instance); } catch { return null; }
    }
}

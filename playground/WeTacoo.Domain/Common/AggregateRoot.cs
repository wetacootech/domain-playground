namespace WeTacoo.Domain.Common;

public abstract class AggregateRoot
{
    private static int _globalCounter;

    public string Id { get; set; } = GenerateId("ar");
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; set; } = 1;

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    private static string GenerateId(string prefix)
    {
        var n = Interlocked.Increment(ref _globalCounter);
        return $"{prefix}-{n:D3}";
    }

    /// <summary>Call from derived constructors or initializers to set a type-specific prefix.</summary>
    protected static string NextId(string prefix)
    {
        var n = Interlocked.Increment(ref _globalCounter);
        return $"{prefix}-{n:D3}";
    }

    public static void ResetCounter() => _globalCounter = 0;
}

public abstract class Entity
{
    private static int _entityCounter;
    public string Id { get; set; } = GenerateEntityId("e");

    private static string GenerateEntityId(string prefix)
    {
        var n = Interlocked.Increment(ref _entityCounter);
        return $"{prefix}-{n:D3}";
    }

    protected static string NextEntityId(string prefix)
    {
        var n = Interlocked.Increment(ref _entityCounter);
        return $"{prefix}-{n:D3}";
    }

    public static void ResetCounter() => _entityCounter = 0;
}

public abstract record ValueObject;

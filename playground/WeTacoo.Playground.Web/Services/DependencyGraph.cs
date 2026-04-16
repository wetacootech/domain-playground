namespace WeTacoo.Playground.Web.Services;

using System.Collections;
using System.Runtime.CompilerServices;
using WeTacoo.Domain.Common;

public enum NodeKind { AR, Entity, ValueObject }

public enum EdgeKind { Contain, Reference }

public record GraphNode(object Instance, string Type, string Id, string Label, NodeKind Kind);

public record GraphEdge(GraphNode From, GraphNode To, string FieldName, bool IsList, EdgeKind Kind);

public record TreeNode(GraphNode Node, string? FromParentField, bool FromParentIsList, List<TreeNode> Children);

public record DependencyGraph(TreeNode Root, List<GraphEdge> Incoming, List<GraphEdge> Outgoing);

public static class DependencyGraphBuilder
{
    private const int MaxTreeDepth = 5;
    private static readonly Type ARBase = typeof(AggregateRoot);
    private static readonly Type EntityBase = typeof(Entity);

    public static DependencyGraph Build(object center, List<(Type ArType, IList Items)> arCollections)
    {
        var root = BuildTree(center, fromField: null, isList: false, currentDepth: 0);

        // Collect all tree nodes for reference matching
        var allTreeNodes = new List<TreeNode>();
        Flatten(root, allTreeNodes);

        var outgoing = new List<GraphEdge>();
        foreach (var tn in allTreeNodes)
            CollectOutgoingForInstance(tn, arCollections, outgoing);

        // Incoming: scan every other AR instance (and their inner entities recursively) for refs
        // matching any AR or Entity node in the tree (VO not addressable).
        var targetByKey = new Dictionary<(string type, string id), GraphNode>();
        foreach (var tn in allTreeNodes)
        {
            if (tn.Node.Kind == NodeKind.ValueObject) continue;
            targetByKey[(tn.Node.Type, tn.Node.Id)] = tn.Node;
        }

        var incoming = new List<GraphEdge>();
        foreach (var (arType, items) in arCollections)
        {
            foreach (var item in items)
            {
                if (item == null) continue;
                if (ReferenceEquals(item, center)) continue;
                var sourceNode = MakeNode(item);
                ScanForIncoming(item, sourceNode, targetByKey, incoming);
            }
        }

        return new DependencyGraph(root, Dedup(incoming), Dedup(outgoing));
    }

    private static TreeNode BuildTree(object instance, string? fromField, bool isList, int currentDepth)
    {
        var node = MakeNode(instance);
        var children = new List<TreeNode>();
        if (currentDepth >= MaxTreeDepth)
            return new TreeNode(node, fromField, isList, children);

        var view = InstanceInspector.Inspect(instance);
        foreach (var p in view.Properties)
        {
            switch (p.Kind)
            {
                case SchemaKind.InnerEntity when p.Value != null:
                    children.Add(BuildTree(p.Value, p.Name, false, currentDepth + 1));
                    break;
                case SchemaKind.InnerEntityList when p.Value is IList el:
                    foreach (var e in el) if (e != null)
                            children.Add(BuildTree(e, p.Name, true, currentDepth + 1));
                    break;
                case SchemaKind.ValueObject when p.Value != null:
                    children.Add(BuildTree(p.Value, p.Name, false, currentDepth + 1));
                    break;
                case SchemaKind.ValueObjectList when p.Value is IList vl:
                    foreach (var v in vl) if (v != null)
                            children.Add(BuildTree(v, p.Name, true, currentDepth + 1));
                    break;
            }
        }
        return new TreeNode(node, fromField, isList, children);
    }

    private static void Flatten(TreeNode n, List<TreeNode> acc)
    {
        acc.Add(n);
        foreach (var c in n.Children) Flatten(c, acc);
    }

    private static GraphNode MakeNode(object instance)
    {
        var t = instance.GetType();
        NodeKind kind;
        string id;
        string label;

        if (t.IsSubclassOf(ARBase))
        {
            kind = NodeKind.AR;
            id = t.GetProperty("Id")?.GetValue(instance) as string ?? "";
            label = InstanceInspector.BuildLabel(instance, t);
        }
        else if (t.IsSubclassOf(EntityBase))
        {
            kind = NodeKind.Entity;
            id = t.GetProperty("Id")?.GetValue(instance) as string ?? "";
            label = InstanceInspector.BuildLabel(instance, t);
        }
        else
        {
            kind = NodeKind.ValueObject;
            id = $"vo-{RuntimeHelpers.GetHashCode(instance):X}";
            var raw = instance.ToString() ?? t.Name;
            // records produce "Type { Prop = Value, ... }"; strip the prefix for brevity
            var braceIdx = raw.IndexOf('{');
            label = braceIdx >= 0 ? raw[braceIdx..].Trim() : raw;
        }

        return new GraphNode(instance, t.Name, id, label, kind);
    }

    private static void CollectOutgoingForInstance(TreeNode tn, List<(Type ArType, IList Items)> arCollections, List<GraphEdge> outgoing)
    {
        var view = InstanceInspector.Inspect(tn.Node.Instance);
        foreach (var p in view.Properties)
        {
            if (p.Kind == SchemaKind.CrossARRef)
            {
                var id = p.Value as string;
                if (!string.IsNullOrEmpty(id) && p.RelatedTypeName != null)
                {
                    var target = Resolve(arCollections, p.RelatedTypeName, id);
                    if (target != null)
                        outgoing.Add(new GraphEdge(tn.Node, MakeNode(target), p.Name, false, EdgeKind.Reference));
                }
            }
            else if (p.Kind == SchemaKind.CrossARRefList && p.Value is IList ids && p.RelatedTypeName != null)
            {
                foreach (var id in ids.Cast<string>())
                {
                    var target = Resolve(arCollections, p.RelatedTypeName, id);
                    if (target != null)
                        outgoing.Add(new GraphEdge(tn.Node, MakeNode(target), p.Name, true, EdgeKind.Reference));
                }
            }
        }
    }

    private static void ScanForIncoming(object instance, GraphNode sourceNode,
        Dictionary<(string, string), GraphNode> targets, List<GraphEdge> incoming, string prefix = "")
    {
        var view = InstanceInspector.Inspect(instance);
        foreach (var p in view.Properties)
        {
            var label = prefix + p.Name;
            switch (p.Kind)
            {
                case SchemaKind.CrossARRef:
                    if (p.RelatedTypeName != null && p.Value is string id && !string.IsNullOrEmpty(id) &&
                        targets.TryGetValue((p.RelatedTypeName, id), out var tn))
                        incoming.Add(new GraphEdge(sourceNode, tn, label, false, EdgeKind.Reference));
                    break;
                case SchemaKind.CrossARRefList:
                    if (p.RelatedTypeName != null && p.Value is IList idList)
                    {
                        foreach (var rid in idList.Cast<string>())
                        {
                            if (targets.TryGetValue((p.RelatedTypeName, rid), out var t))
                                incoming.Add(new GraphEdge(sourceNode, t, label, true, EdgeKind.Reference));
                        }
                    }
                    break;
                case SchemaKind.InnerEntity:
                    if (p.Value != null) ScanForIncoming(p.Value, sourceNode, targets, incoming, label + ".");
                    break;
                case SchemaKind.InnerEntityList:
                    if (p.Value is IList inner)
                    {
                        foreach (var e in inner)
                            if (e != null) ScanForIncoming(e, sourceNode, targets, incoming, label + "[].");
                    }
                    break;
            }
        }
    }

    private static List<GraphEdge> Dedup(List<GraphEdge> list)
        => list.GroupBy(e => (e.From.Type, e.From.Id, e.To.Type, e.To.Id, e.FieldName, e.Kind))
               .Select(g => g.First()).ToList();

    private static object? Resolve(List<(Type ArType, IList Items)> arCollections, string typeName, string id)
    {
        var bucket = arCollections.FirstOrDefault(x => x.ArType.Name == typeName);
        if (bucket.Items == null) return null;
        foreach (var item in bucket.Items)
        {
            var itemId = item?.GetType().GetProperty("Id")?.GetValue(item) as string;
            if (itemId == id) return item;
        }
        return null;
    }

    public static string BcOf(Type t)
    {
        var parts = (t.Namespace ?? "").Split('.');
        var idx = Array.IndexOf(parts, "Domain");
        return idx >= 0 && parts.Length > idx + 1 ? parts[idx + 1] : "Other";
    }
}

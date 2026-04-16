namespace WeTacoo.Domain.Execution;
using WeTacoo.Domain.Common;

/// <summary>
/// ObjectGroup (DDD5 §5.7, "Gruppo oggetto"). AR per raggruppare PhysicalObject con attributi comuni
/// (stessa scatola sigillata, stesso lotto, ecc.). Gli Object puntano al gruppo tramite GroupId.
/// </summary>
public class ObjectGroup : AggregateRoot
{
    public ObjectGroup() { Id = NextId("ogrp"); }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? DealId { get; set; }
    public List<string> ObjectIds { get; set; } = [];
    public List<string> Photos { get; set; } = [];
    public string? Notes { get; set; }
}

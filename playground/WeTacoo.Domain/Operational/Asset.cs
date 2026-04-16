namespace WeTacoo.Domain.Operational;
using WeTacoo.Domain.Common;

/// <summary>
/// Asset operativo (DDD5 §4.5). AR per attrezzature non-veicolo (carrelli, transpallet, gru, ecc.).
/// Vehicle resta AR separato nel codice per compatibilità; Asset copre il resto delle risorse non-veicolo.
/// </summary>
public class Asset : AggregateRoot
{
    public Asset() { Id = NextId("asset"); }
    public string Name { get; set; } = "";
    public string AssetType { get; set; } = "carrello"; // carrello, transpallet, gru, ...
    public string? Description { get; set; }
    public string? AreaId { get; set; }
    public string Status { get; set; } = "Available"; // Available, InUse, Maintenance, OutOfService
    public List<string> Damages { get; set; } = [];
    public List<string> Checks { get; set; } = [];
    public decimal? PartnerCost { get; set; }
    public string? PartnerName { get; set; }
}

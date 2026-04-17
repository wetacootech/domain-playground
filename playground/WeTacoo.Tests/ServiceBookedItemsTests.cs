using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;

namespace WeTacoo.Tests;

/// <summary>
/// Lista oggetti stimati + volume dichiarato su ServiceBooked (DDD5 §2.1, review 2026-04-17).
/// Regole:
/// - Items e DeclaredVolume sono alternativi: aggiungere un item azzera DeclaredVolume; settare DeclaredVolume svuota Items.
/// - Mutazioni bloccate quando Status != ToAccept (dati venduti immutabili dopo finalizzazione, §10d).
/// - EstimatedVolume: somma Items se presenti, altrimenti DeclaredVolume (0 se null).
/// </summary>
public class ServiceBookedItemsTests
{
    [Fact]
    public void AddItem_SetsItem_AndClearsDeclaredVolume()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.SetDeclaredVolume(15m);
        Assert.Equal(15m, svc.DeclaredVolume);

        svc.AddItem("tpl-1", "Armadio 2 ante", 2, 1.8m);

        Assert.Single(svc.Items);
        Assert.Null(svc.DeclaredVolume);
        Assert.Equal(3.6m, svc.EstimatedVolume);
    }

    [Fact]
    public void SetDeclaredVolume_ClearsItems()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Scatola", 3, 0.06m);
        Assert.Single(svc.Items);

        svc.SetDeclaredVolume(20m);

        Assert.Empty(svc.Items);
        Assert.Equal(20m, svc.DeclaredVolume);
        Assert.Equal(20m, svc.EstimatedVolume);
    }

    [Fact]
    public void EstimatedVolume_PrefersItemsWhenPresent()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Letto matrimoniale", 1, 1.5m);
        svc.AddItem("tpl-2", "Comodino", 2, 0.2m);

        Assert.Equal(1.9m, svc.EstimatedVolume);
        Assert.Null(svc.DeclaredVolume);
    }

    [Fact]
    public void EstimatedVolume_ZeroIfNothingSet()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        Assert.Equal(0m, svc.EstimatedVolume);
    }

    [Fact]
    public void AddItem_SameTemplate_IncrementsQuantity()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Sedia", 2, 0.15m);
        svc.AddItem("tpl-1", "Sedia", 3, 0.15m);

        Assert.Single(svc.Items);
        Assert.Equal(5, svc.Items[0].Quantity);
        Assert.Equal(0.75m, svc.EstimatedVolume);
    }

    [Fact]
    public void UpdateItemQuantity_ZeroRemoves()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Divano", 1, 2m);
        svc.UpdateItemQuantity("tpl-1", 0);

        Assert.Empty(svc.Items);
    }

    [Fact]
    public void RemoveItem_TogliesFromList()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-a", "A", 1, 1m);
        svc.AddItem("tpl-b", "B", 1, 1m);
        svc.RemoveItem("tpl-a");

        Assert.Single(svc.Items);
        Assert.Equal("tpl-b", svc.Items[0].ObjectTemplateId);
    }

    [Fact]
    public void Mutations_BlockedAfterAcceptance()
    {
        // Dopo AccettaServizio -> ToComplete, Items e DeclaredVolume sono immutabili
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Scrivania", 1, 0.8m);
        svc.AccettaServizio(); // ToComplete

        svc.AddItem("tpl-2", "Libreria", 1, 1.2m);
        svc.RemoveItem("tpl-1");
        svc.SetDeclaredVolume(10m);

        Assert.Single(svc.Items);
        Assert.Equal("tpl-1", svc.Items[0].ObjectTemplateId);
        Assert.Null(svc.DeclaredVolume);
    }

    [Fact]
    public void Mutations_BlockedAfterInspectionRequest()
    {
        var svc = new ServiceBooked { Type = ServiceBookedType.Ritiro };
        svc.AddItem("tpl-1", "Frigorifero", 1, 0.8m);
        svc.RichiediSopralluogo("wo-insp-1"); // -> WaitingInspection

        svc.AddItem("tpl-2", "Lavatrice", 1, 0.5m);

        Assert.Single(svc.Items);
    }

    [Fact]
    public void TotalVolume_OnItem_MatchesQtyTimesUnit()
    {
        var item = new ServiceBookedItem("tpl", "Tavolo", 3, 1m);
        Assert.Equal(3m, item.TotalVolume);
    }
}

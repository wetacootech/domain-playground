using WeTacoo.Domain.Commercial.Entities;
using WeTacoo.Domain.Commercial.Enums;

namespace WeTacoo.Tests;

/// <summary>
/// Cascade aggregato sullo stato Quotation post-esecuzione (§10d, review 2026-04-17):
/// se ANCHE UN SOLO ServiceBooked ha registrato CompletionData (= differenze rilevate in esecuzione),
/// la Quotation va in ToAdjust, non Completed, indipendentemente dall'ordine degli eventi.
/// </summary>
public class UC_CascadeAdjustmentTests
{
    private static ServiceBooked CompleteSB(ServiceBookedType type, bool withDifferences)
    {
        var sb = new ServiceBooked { Type = type };
        sb.AccettaServizio();       // ToAccept -> ToComplete
        sb.SegnaComePronto();       // ToComplete -> Ready
        sb.ServizioCompletato();    // Ready -> Completed
        if (withDifferences)
            sb.CompletionData = new CompletionRecord(28m, 0, "differenza volume", DateTime.UtcNow);
        return sb;
    }

    private static void ApplyHandlerLogic(Quotation q)
    {
        // Simula la logica del handler ServizioCompletatoEvent in PlaygroundState post-fix.
        var allDone = q.Services.All(s => s.Status == ServiceBookedStatus.Completed);
        if (!allDone) return;
        if (q.Status != QuotationStatus.Finalized) return;
        bool anyHasDifferences = q.Services.Any(s => s.CompletionData != null);
        if (anyHasDifferences) q.MarkToAdjust();
        else q.Complete();
    }

    [Fact]
    public void AllServicesWithoutDifferences_Completes_Quotation()
    {
        var q = new Quotation { DealId = "d" };
        q.Services.Add(CompleteSB(ServiceBookedType.Ritiro, withDifferences: false));
        q.Services.Add(CompleteSB(ServiceBookedType.Consegna, withDifferences: false));
        q.Confirm(); q.Finalize();

        ApplyHandlerLogic(q);

        Assert.Equal(QuotationStatus.Completed, q.Status);
    }

    [Fact]
    public void AllServicesWithDifferences_MovesQuotationToAdjust()
    {
        var q = new Quotation { DealId = "d" };
        q.Services.Add(CompleteSB(ServiceBookedType.Ritiro, withDifferences: true));
        q.Services.Add(CompleteSB(ServiceBookedType.Consegna, withDifferences: true));
        q.Confirm(); q.Finalize();

        ApplyHandlerLogic(q);

        Assert.Equal(QuotationStatus.ToAdjust, q.Status);
    }

    [Fact]
    public void MixedServices_OneWithDifferences_MovesQuotationToAdjust()
    {
        // Caso critico (review 2026-04-17): un SB ha CompletionData, l'altro no.
        // Prima del fix la Quotation sarebbe stata Completata se l'ultimo evento era "senza differenze".
        var q = new Quotation { DealId = "d" };
        q.Services.Add(CompleteSB(ServiceBookedType.Ritiro, withDifferences: true));  // differenze
        q.Services.Add(CompleteSB(ServiceBookedType.Consegna, withDifferences: false)); // nessuna differenza
        q.Confirm(); q.Finalize();

        ApplyHandlerLogic(q);

        Assert.Equal(QuotationStatus.ToAdjust, q.Status);
    }

    [Fact]
    public void MixedServices_Reversed_StillGoesToAdjust()
    {
        // Ordine inverso (senza differenze prima, con differenze dopo) -> sempre ToAdjust
        var q = new Quotation { DealId = "d" };
        q.Services.Add(CompleteSB(ServiceBookedType.Ritiro, withDifferences: false));
        q.Services.Add(CompleteSB(ServiceBookedType.Consegna, withDifferences: true));
        q.Confirm(); q.Finalize();

        ApplyHandlerLogic(q);

        Assert.Equal(QuotationStatus.ToAdjust, q.Status);
    }
}

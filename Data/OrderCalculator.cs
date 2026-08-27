using KanchimeshAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Data;

public static class OrderCalculator
{
    public static void Recalculate(SalesOrder order)
    {
        foreach (var item in order.Items)
        {
            item.LineSubtotal = Round(item.Quantity * item.Rate);
            item.TaxAmount = Round(item.LineSubtotal * (item.IgstRate + item.SgstRate + item.CgstRate) / 100m);
            item.LineTotal = Round(item.LineSubtotal + item.TaxAmount);
        }

        order.Subtotal = Round(order.Items.Sum(x => x.LineSubtotal));
        var discount = Math.Min(Math.Max(order.DiscountAmount, 0m), order.Subtotal);
        order.DiscountAmount = Round(discount);
        order.FreightAmount = Round(Math.Max(order.FreightAmount, 0m));

        var itemTax = order.Items.Sum(x => x.TaxAmount);
        var effectiveTaxRate = order.Subtotal == 0m ? 0m : itemTax / order.Subtotal;
        order.TaxAmount = Round((order.Subtotal - order.DiscountAmount + order.FreightAmount) * effectiveTaxRate);
        order.GrandTotal = Round(order.Subtotal - order.DiscountAmount + order.FreightAmount + order.TaxAmount);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static async Task SyncOrderCompletionAsync(Microsoft.EntityFrameworkCore.DbContext database, SalesOrder order, CancellationToken cancellationToken)
    {
        await database.Entry(order).Collection(o => o.Payments).LoadAsync(cancellationToken);

        var paidAmount = order.Payments.Where(p => !p.IsAdvance).Sum(p => p.Amount);
        
        bool isFullyPaid = paidAmount >= order.GrandTotal && order.GrandTotal > 0;
        
        // Auto-complete if fully paid
        if (isFullyPaid && order.Status != "Cancelled")
        {
            order.Status = "Completed";
        }
        else if (!isFullyPaid && order.Status == "Completed")
        {
            order.Status = "Pending";
        }
    }
}

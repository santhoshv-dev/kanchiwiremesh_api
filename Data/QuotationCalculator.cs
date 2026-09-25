using KanchimeshAPI.Models;

namespace KanchimeshAPI.Data;

public static class QuotationCalculator
{
    public static void Recalculate(Quotation quotation)
    {
        foreach (var item in quotation.Items)
        {
            item.LineSubtotal = Round(item.Quantity * item.Rate);
            item.TaxAmount = Round(item.LineSubtotal * (item.IgstRate + item.SgstRate + item.CgstRate) / 100m);
            item.LineTotal = Round(item.LineSubtotal + item.TaxAmount);
        }

        quotation.Subtotal = Round(quotation.Items.Sum(x => x.LineSubtotal));
        var discount = Math.Min(Math.Max(quotation.DiscountAmount, 0m), quotation.Subtotal);
        quotation.DiscountAmount = Round(discount);
        quotation.FreightAmount = Round(Math.Max(quotation.FreightAmount, 0m));

        var itemTax = quotation.Items.Sum(x => x.TaxAmount);
        var effectiveTaxRate = quotation.Subtotal == 0m ? 0m : itemTax / quotation.Subtotal;
        quotation.TaxAmount = Round((quotation.Subtotal - quotation.DiscountAmount + quotation.FreightAmount) * effectiveTaxRate);
        quotation.GrandTotal = Round(quotation.Subtotal - quotation.DiscountAmount + quotation.FreightAmount + quotation.TaxAmount);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

using KanchimeshAPI.Data;
using KanchimeshAPI.Models;
using Xunit;

namespace KanchimeshAPI.Tests;

public sealed class OrderCalculatorRoundingTests
{
    [Theory]
    [InlineData(100.40, 100)] // Below 50 paisa stays with old amount
    [InlineData(100.49, 100)] // 49 paisa stays with old amount
    [InlineData(100.50, 101)] // 50 paisa converts to next rupee
    [InlineData(100.51, 101)] // 51 paisa converts to next rupee
    [InlineData(100.75, 101)] // Above 50 paisa converts to next rupee
    public void OrderCalculator_RoundsGrandTotal_HalfAwayFromZero(double rate, decimal expectedGrandTotal)
    {
        var decimalRate = (decimal)rate;
        var order = new SalesOrder
        {
            Items =
            [
                new SalesOrderItem
                {
                    Description = "Test item",
                    Quantity = 1m,
                    Rate = decimalRate,
                    IgstRate = 0m,
                    SgstRate = 0m,
                    CgstRate = 0m,
                }
            ]
        };

        OrderCalculator.Recalculate(order);

        Assert.Equal(expectedGrandTotal, order.GrandTotal);
    }

    [Theory]
    [InlineData(1000.00, 18.0, 1180)] // 1000 + 180 tax = 1180
    [InlineData(100.35, 18.0, 118)]   // 100.35 + 18.06 tax = 118.41 -> 118
    [InlineData(100.45, 18.0, 119)]   // 100.45 + 18.08 tax = 118.53 -> 119
    public void OrderCalculator_RoundsGrandTotal_WithTax(double rate, double igstRate, decimal expectedGrandTotal)
    {
        var order = new SalesOrder
        {
            Items =
            [
                new SalesOrderItem
                {
                    Description = "Test item",
                    Quantity = 1m,
                    Rate = (decimal)rate,
                    IgstRate = (decimal)igstRate,
                    SgstRate = 0m,
                    CgstRate = 0m,
                }
            ]
        };

        OrderCalculator.Recalculate(order);

        Assert.Equal(expectedGrandTotal, order.GrandTotal);
    }

    [Theory]
    [InlineData(100.40, 100)] // Below 50 paisa stays with old amount
    [InlineData(100.50, 101)] // 50 paisa converts to next rupee
    [InlineData(100.75, 101)] // Above 50 paisa converts to next rupee
    public void QuotationCalculator_RoundsGrandTotal_HalfAwayFromZero(double rate, decimal expectedGrandTotal)
    {
        var quotation = new Quotation
        {
            Items =
            [
                new QuotationItem
                {
                    Description = "Test quotation item",
                    Quantity = 1m,
                    Rate = (decimal)rate,
                    IgstRate = 0m,
                    SgstRate = 0m,
                    CgstRate = 0m,
                }
            ]
        };

        QuotationCalculator.Recalculate(quotation);

        Assert.Equal(expectedGrandTotal, quotation.GrandTotal);
    }
}

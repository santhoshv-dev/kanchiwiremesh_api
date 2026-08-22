using KanchimeshAPI.Data;
using Microsoft.AspNetCore.Mvc;

namespace KanchimeshAPI.Controllers;

[Route("api/lookups")]
public sealed class LookupsController : ApiControllerBase
{
    [HttpGet("workflow-options")]
    public IActionResult GetWorkflowOptions() => Ok(new
    {
        enquiryStatuses = WorkflowValues.EnquiryStatuses,
        orderStatuses = WorkflowValues.OrderStatuses,
        paymentMethods = WorkflowValues.PaymentMethods,
        productCategories = new[]
        {
            "Crusher Mesh", "Vibrating Screen Mesh", "Mining Mesh", "Woven Wire Mesh", "Roller", "Custom Mesh"
        },
        meshTypes = new[] { "Square", "Rectangular", "Diamond", "Hexagonal", "Custom" }
    });
}

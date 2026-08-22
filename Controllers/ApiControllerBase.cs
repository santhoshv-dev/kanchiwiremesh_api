using Microsoft.AspNetCore.Mvc;

namespace KanchimeshAPI.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult ValidationError(string field, string message) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        });

    protected static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Clamp(page, 1, 100000), Math.Clamp(pageSize, 1, 100));

    protected static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

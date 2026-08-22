using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Infrastructure;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled API exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        var isConflict = exception is DbUpdateException or DbUpdateConcurrencyException;
        httpContext.Response.StatusCode = isConflict
            ? StatusCodes.Status409Conflict
            : StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = httpContext.Response.StatusCode,
                Title = isConflict ? "The record was changed by another request." : "An unexpected error occurred.",
                Detail = isConflict
                    ? "Refresh the data and try the request again."
                    : "The request could not be completed. Please try again."
            },
            Exception = exception
        });
    }
}

using Agentiva.BuildingBlocks.Application.Behaviors;
using Agentiva.BuildingBlocks.Application.Exceptions;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agentiva.BuildingBlocks.ServiceDefaults.Errors;

/// <summary>
/// Converts unhandled exceptions into RFC 9457 problem documents.
/// </summary>
/// <remarks>
/// <para>
/// Every response carries the correlation identifier, so a user reporting a
/// failure gives support the exact key needed to find the request's logs and
/// trace.
/// </para>
/// <para>
/// <see cref="ICorrelationContext"/> is resolved from
/// <see cref="HttpContext.RequestServices"/> rather than injected. ASP.NET Core
/// registers an <see cref="IExceptionHandler"/> as a <em>singleton</em>, and a
/// singleton cannot capture a scoped dependency — doing so fails DI scope
/// validation at startup, and would otherwise pin one request's correlation
/// identifier onto every later error response.
/// </para>
/// <para>
/// Exception detail is never returned for an unexpected failure. A raw .NET
/// exception message routinely contains connection strings, host names, SQL
/// fragments and file paths; in a financial API that is both an information
/// leak and an aid to an attacker mapping the system. Expected failures —
/// validation, a domain rule, an idempotency conflict — do return their message,
/// because those strings are written by us for the caller.
/// </para>
/// </remarks>
public sealed class AgentivaExceptionHandler(ILogger<AgentivaExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Map(exception, httpContext);

        // Per-request resolution; see the remarks on this type.
        var correlation = httpContext.RequestServices.GetService<ICorrelationContext>();

        if (correlation is not null)
        {
            problem.Extensions["correlationId"] = correlation.CorrelationId;
            problem.Extensions["requestId"] = correlation.RequestId;
        }

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled {ExceptionType} on {Method} {Path}",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(
                "{ExceptionType} on {Method} {Path}: {Reason}",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static ProblemDetails Map(Exception exception, HttpContext httpContext) => exception switch
    {
        AgentivaValidationException validation => new ValidationProblemDetails(
            validation.Errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://agentiva.local/problems/validation",
            Instance = httpContext.Request.Path
        },

        // A replayed or poisoned idempotency key. 409 tells the caller that
        // retrying with the same key will not help.
        IdempotencyConflictException conflict => new ProblemDetails
        {
            Title = "Idempotency conflict",
            Detail = conflict.Message,
            Status = StatusCodes.Status409Conflict,
            Type = "https://agentiva.local/problems/idempotency-conflict",
            Instance = httpContext.Request.Path,
            Extensions = { ["code"] = conflict.Code }
        },

        // A broken domain invariant: syntactically valid input that the domain
        // refuses. 422 rather than 400 distinguishes it from a malformed body.
        DomainException domain => new ProblemDetails
        {
            Title = "Domain rule violation",
            Detail = domain.Message,
            Status = StatusCodes.Status422UnprocessableEntity,
            Type = "https://agentiva.local/problems/domain-rule",
            Instance = httpContext.Request.Path,
            Extensions = { ["code"] = domain.Code }
        },

        OperationCanceledException => new ProblemDetails
        {
            Title = "Request cancelled",
            Status = 499, // nginx convention: client closed request
            Type = "https://agentiva.local/problems/cancelled",
            Instance = httpContext.Request.Path
        },

        // Deliberately opaque. See the remarks on this type.
        _ => new ProblemDetails
        {
            Title = "An unexpected error occurred.",
            Detail = "The failure has been logged. Quote the correlation id when reporting it.",
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://agentiva.local/problems/internal",
            Instance = httpContext.Request.Path
        }
    };
}

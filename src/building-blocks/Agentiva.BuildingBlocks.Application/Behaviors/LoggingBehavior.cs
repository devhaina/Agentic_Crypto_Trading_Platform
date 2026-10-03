using System.Diagnostics;
using Agentiva.BuildingBlocks.Application.Mediator;
using Agentiva.BuildingBlocks.Common.Correlation;
using Agentiva.BuildingBlocks.Common.Results;
using Microsoft.Extensions.Logging;

namespace Agentiva.BuildingBlocks.Application.Behaviors;

/// <summary>
/// Emits a structured log record for every request, with correlation identifiers
/// and elapsed time.
/// </summary>
/// <remarks>
/// Logs the request <em>type name</em> and never the request body. Request
/// payloads in this platform can contain balances and account identifiers, and
/// nothing in the trading path is permitted to leak those into log storage.
/// Per-field detail is captured in the audit service instead, which has its own
/// retention and access controls.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICorrelationContext correlation)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var kind = request switch
        {
            IFinancialCommand<TResponse> => "FinancialCommand",
            ICommand<TResponse> => "Command",
            IQuery<TResponse> => "Query",
            _ => "Request"
        };

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlation.CorrelationId,
            ["RequestId"] = correlation.RequestId,
            ["AgentRunId"] = correlation.AgentRunId,
            ["RequestName"] = requestName,
            ["RequestKind"] = kind
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            // An expected domain failure is logged at Warning with its reason
            // code, not as an error: a risk rejection is the system working.
            if (response is Result { IsFailure: true } failed)
            {
                logger.LogWarning(
                    "{RequestName} completed with failure {ReasonCode} in {ElapsedMs}ms",
                    requestName,
                    failed.Error.Code,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "{RequestName} completed in {ElapsedMs}ms",
                    requestName,
                    stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogInformation(
                "{RequestName} cancelled after {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "{RequestName} threw {ExceptionType} after {ElapsedMs}ms",
                requestName,
                ex.GetType().Name,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

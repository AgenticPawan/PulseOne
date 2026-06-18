using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace PulseOne.Application.Behaviors;

/// <summary>
/// First behavior in the pipeline: structured entry/exit logging with timing for every request.
/// Logs the request type name (not its contents) to avoid leaking PII into the log stream.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName}", requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            stopwatch.Stop();
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMilliseconds}ms",
                requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Failed {RequestName} after {ElapsedMilliseconds}ms",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

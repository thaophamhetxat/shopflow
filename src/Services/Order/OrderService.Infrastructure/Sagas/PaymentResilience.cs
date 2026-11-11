using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Fallback;
using Polly.Timeout;

namespace OrderService.Infrastructure.Sagas;

/// <summary>
/// Cấu hình mặc định cho resilience policy gọi PaymentService, đảm bảo timeout, retry và circuit breaker đúng theo Day 54.
/// </summary>
public sealed class PaymentResilienceOptions
{
    public static PaymentResilienceOptions Default { get; } = new();

    public TimeSpan TimeoutDuration { get; init; } = TimeSpan.FromSeconds(5);
    public int RetryCount { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int MinimumThroughput { get; init; } = 5;
    public double FailureRatio { get; init; } = 1.0;
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Pipeline resilience của PaymentService: fallback ngoài cùng, timeout 5s, retry 3 lần và circuit breaker 30s.
/// </summary>
public static class PaymentResilience
{
    public static void ConfigurePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ILogger logger,
        PaymentResilienceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logger);

        var effectiveOptions = options ?? PaymentResilienceOptions.Default;

        builder.AddFallback(new FallbackStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
                .HandleResult(response => (int)response.StatusCode >= 500),
            FallbackAction = args =>
            {
                var reason = args.Outcome.Exception is not null
                    ? args.Outcome.Exception.GetType().Name
                    : $"HTTP {(int)(args.Outcome.Result?.StatusCode ?? HttpStatusCode.InternalServerError)}";

                logger.LogWarning("Payment fallback triggered because {Reason}.", reason);

                var fallbackResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"PaymentUnavailable\"}", Encoding.UTF8, "application/json")
                };

                return Outcome.FromResultAsValueTask(fallbackResponse);
            }
        });

        builder.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Timeout = effectiveOptions.TimeoutDuration,
            OnTimeout = args =>
            {
                logger.LogWarning("Payment timeout after {Timeout}.", effectiveOptions.TimeoutDuration);
                return default;
            }
        });

        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = effectiveOptions.RetryCount,
            Delay = effectiveOptions.RetryDelay,
            UseJitter = false,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => (int)response.StatusCode >= 500),
            OnRetry = args =>
            {
                logger.LogWarning(
                    "Retry {Attempt}/{MaxRetryAttempts} after {Delay}ms because {Reason}.",
                    args.AttemptNumber,
                    effectiveOptions.RetryCount,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception is not null ? args.Outcome.Exception.GetType().Name : $"HTTP {(int)(args.Outcome.Result?.StatusCode ?? HttpStatusCode.InternalServerError)}");
                return default;
            }
        });

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = effectiveOptions.SamplingDuration,
            MinimumThroughput = effectiveOptions.MinimumThroughput,
            FailureRatio = effectiveOptions.FailureRatio,
            BreakDuration = effectiveOptions.BreakDuration,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => (int)response.StatusCode >= 500),
            OnOpened = args =>
            {
                logger.LogWarning("Circuit breaker: Closed -> Open");
                return default;
            },
            OnHalfOpened = args =>
            {
                logger.LogWarning("Circuit breaker: Open -> HalfOpen");
                return default;
            },
            OnClosed = args =>
            {
                logger.LogWarning("Circuit breaker: HalfOpen -> Closed");
                return default;
            }
        });
    }
}

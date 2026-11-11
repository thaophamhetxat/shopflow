using System.Net.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace OrderService.Infrastructure.Sagas;

public sealed class PaymentResilienceOptions
{
    public static PaymentResilienceOptions Default { get; } = new();

    public TimeSpan TimeoutDuration { get; init; } = TimeSpan.FromSeconds(10);
    public int RetryCount { get; init; } = 2;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
    public int MinimumThroughput { get; init; } = 5;
    public double FailureRatio { get; init; } = 0.5;
}

public static class PaymentResilience
{
    public static void ConfigurePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ILogger logger,
        PaymentResilienceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var effectiveOptions = options ?? PaymentResilienceOptions.Default;

        builder.AddTimeout(effectiveOptions.TimeoutDuration);

        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = effectiveOptions.RetryCount,
            Delay = effectiveOptions.RetryDelay,
            UseJitter = true,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => !response.IsSuccessStatusCode)
        });

        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = effectiveOptions.MinimumThroughput,
            FailureRatio = effectiveOptions.FailureRatio,
            BreakDuration = effectiveOptions.BreakDuration,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response => !response.IsSuccessStatusCode)
        });
    }
}

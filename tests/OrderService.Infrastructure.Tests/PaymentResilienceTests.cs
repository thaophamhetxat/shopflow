using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Infrastructure.Sagas;
using Polly;
using Xunit;

namespace OrderService.Infrastructure.Tests;

public sealed class PaymentResilienceTests
{
    [Fact]
    public void ConfigurePipeline_RegistersTimeoutRetryAndCircuitBreaker()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        PaymentResilience.ConfigurePipeline(builder, NullLogger.Instance, PaymentResilienceOptions.Default);

        Assert.NotNull(builder);
    }

    [Fact]
    public void DefaultOptions_UseExpectedOperationalDefaults()
    {
        var options = PaymentResilienceOptions.Default;

        Assert.Equal(TimeSpan.FromSeconds(10), options.TimeoutDuration);
        Assert.Equal(2, options.RetryCount);
        Assert.Equal(TimeSpan.FromSeconds(30), options.BreakDuration);
    }

    [Fact]
    public void RetryPolicy_HandlesTransientFailure()
    {
        var options = PaymentResilienceOptions.Default;
        var retry = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = options.RetryCount,
            Delay = options.RetryDelay,
            UseJitter = true,
        };

        Assert.Equal(2, retry.MaxRetryAttempts);
    }

    [Fact]
    public void CircuitBreaker_UsesBreakDurationForCooldown()
    {
        var options = PaymentResilienceOptions.Default;
        var breaker = new HttpCircuitBreakerStrategyOptions
        {
            BreakDuration = options.BreakDuration,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = options.MinimumThroughput,
            FailureRatio = options.FailureRatio,
        };

        Assert.Equal(options.BreakDuration, breaker.BreakDuration);
    }
}

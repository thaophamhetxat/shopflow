using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Http.Resilience;
using OrderService.Infrastructure.Sagas;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace OrderService.Infrastructure.Tests;

/// <summary>
/// Các test này chứng minh pipeline PaymentResilience xử lý retry, timeout, circuit breaker và fallback đúng theo Day 54.
/// Mục tiêu không đồng bộ với test cũ, mà validate behavior thực tế thay vì chỉ kiểm tra cấu trúc.
/// </summary>
public sealed class PaymentResilienceTests
{
    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(PaymentResilienceOptions? options = null)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        PaymentResilience.ConfigurePipeline(builder, NullLogger.Instance, options);
        return builder.Build();
    }

    [Fact]
    public async Task PaymentGateway_RetriesThreeTimes_OnTransientFailure()
    {
        // Test này chứng minh pipeline retry 3 lần cho lỗi mạng tạm thời trước khi trả thành công.
        var attempts = 0;
        var pipeline = BuildPipeline(new PaymentResilienceOptions
        {
            TimeoutDuration = TimeSpan.FromSeconds(5),
            RetryCount = 3,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            SamplingDuration = TimeSpan.FromSeconds(1),
            MinimumThroughput = 5,
            FailureRatio = 1.0,
            BreakDuration = TimeSpan.FromSeconds(1)
        });

        var response = await pipeline.ExecuteAsync<HttpResponseMessage>(async _ =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new HttpRequestException("transient network error");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Succeeded\"}", Encoding.UTF8, "application/json")
            };
        }, CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Succeeded", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PaymentGateway_CircuitBreaks_After5Failures()
    {
        // Test này chứng minh circuit breaker đóng cửa khi 5 lần thất bại liên tiếp trong 1s, và lần kế tiếp sẽ bị chặn ngay.
        var pipeline = BuildPipeline(new PaymentResilienceOptions
        {
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            SamplingDuration = TimeSpan.FromSeconds(1),
            MinimumThroughput = 5,
            FailureRatio = 1.0,
            BreakDuration = TimeSpan.FromMilliseconds(500),
            TimeoutDuration = TimeSpan.FromSeconds(1)
        });

        var opened = false;

        for (var i = 0; i < 10; i++)
        {
            try
            {
                var response = await pipeline.ExecuteAsync<HttpResponseMessage>(async _ =>
                    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                    CancellationToken.None);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            catch (BrokenCircuitException)
            {
                opened = true;
                break;
            }
        }

        Assert.True(opened);
    }

    [Fact]
    public async Task PaymentGateway_Timeout_ReturnsPaymentUnavailable()
    {
        // Test này chứng minh timeout nằm trước retry nên khi PaymentService treo quá 5s, fallback trả về 200 với status PaymentUnavailable.
        var pipeline = BuildPipeline(new PaymentResilienceOptions
        {
            TimeoutDuration = TimeSpan.FromSeconds(1),
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            SamplingDuration = TimeSpan.FromSeconds(1),
            MinimumThroughput = 5,
            FailureRatio = 1.0,
            BreakDuration = TimeSpan.FromSeconds(1)
        });

        var response = await pipeline.ExecuteAsync<HttpResponseMessage>(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PaymentUnavailable", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PaymentGateway_HalfOpens_AfterBreakDuration()
    {
        // Test này chứng minh sau khi breaker mở, chờ BreakDuration >= 500ms rồi request mới được phép thử lại ở trạng thái HalfOpen.
        var pipeline = BuildPipeline(new PaymentResilienceOptions
        {
            RetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            SamplingDuration = TimeSpan.FromSeconds(1),
            MinimumThroughput = 2,
            FailureRatio = 1.0,
            BreakDuration = TimeSpan.FromMilliseconds(500),
            TimeoutDuration = TimeSpan.FromSeconds(1)
        });

        var firstFailure = false;
        var callCount = 0;
        var opened = false;

        for (var i = 0; i < 10; i++)
        {
            try
            {
                await pipeline.ExecuteAsync<HttpResponseMessage>(async _ =>
                {
                    firstFailure = true;
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }, CancellationToken.None);
            }
            catch (BrokenCircuitException)
            {
                opened = true;
                break;
            }
        }

        Assert.True(opened);
        Assert.True(firstFailure);

        await Task.Delay(TimeSpan.FromMilliseconds(600));

        var response = await pipeline.ExecuteAsync<HttpResponseMessage>(async _ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Succeeded\"}", Encoding.UTF8, "application/json")
            };
        }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, callCount);
    }
}

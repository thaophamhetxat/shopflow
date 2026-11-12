// File này gắn middleware đo HTTP và endpoint scrape Prometheus cho từng API.
// Layer BuildingBlocks dùng đúng tên metric, nhãn method/code/handler và chỉ xuất hai metric HTTP đã chốt.
// Day 65 giữ gauge in-progress nội bộ ngoài registry scrape để endpoint chỉ công bố bốn metric yêu cầu.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Prometheus;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Extension bật HTTP metrics và endpoint Prometheus dùng chung cho các API ShopFlow.
/// </summary>
public static class ShopFlowMetricsExtensions
{
    private static readonly Counter HttpRequests =
        Metrics.CreateCounter(
            "http_requests_total",
            "Total number of HTTP requests processed.",
            new[] { "method", "code", "handler" });

    private static readonly Histogram HttpRequestDuration =
        Metrics.CreateHistogram(
            "http_request_duration_seconds",
            "Duration of HTTP requests in seconds.",
            new[] { "method", "code", "handler" });

    /// <summary>
    /// Cài middleware HTTP metrics và ánh xạ endpoint scrape mặc định tại /metrics.
    /// </summary>
    /// <param name="app">Web application đang khởi động.</param>
    /// <returns>Web application sau khi đã gắn middleware và endpoint metrics.</returns>
    public static WebApplication UseShopFlowMetrics(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Force creation of nghiệp vụ counters at API startup so /metrics publishes them before the first business request.
        _ = ShopFlowMetrics.OrdersCreated;
        _ = ShopFlowMetrics.SagaRuns;

        app.UseHttpMetrics(options =>
        {
            // Tắt gauge mặc định để endpoint scrape chỉ xuất counter và histogram HTTP cùng hai counter nghiệp vụ.
            options.InProgress.Enabled = false;
            options.RequestCount.Counter = HttpRequests;
            options.RequestDuration.Histogram = HttpRequestDuration;
            options.AddCustomLabel("handler", context => context.GetEndpoint()?.DisplayName ?? "unmatched");
        });

        app.MapMetrics();
        return app;
    }
}

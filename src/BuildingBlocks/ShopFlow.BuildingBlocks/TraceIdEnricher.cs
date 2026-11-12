// File này bổ sung TraceId và SpanId vào mỗi log event Serilog khi Activity đang hoạt động.
// Layer BuildingBlocks dùng enricher chung để liên kết JSON log với trace OpenTelemetry của Day 64.
// Day 65 thay helper log scope tạm bằng enrichment tự động cho các API ShopFlow.

using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Bổ sung định danh trace hiện hành vào log event Serilog.
/// </summary>
public sealed class TraceIdEnricher : ILogEventEnricher
{
    /// <summary>
    /// Thêm TraceId và SpanId để log JSON có thể được đối chiếu với trace Jaeger.
    /// </summary>
    /// <param name="logEvent">Sự kiện log cần enrich.</param>
    /// <param name="propertyFactory">Factory tạo thuộc tính log có kiểu.</param>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        // Dùng cùng trace context của Activity để nối JSON log với span đã export sang Jaeger.
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
    }
}

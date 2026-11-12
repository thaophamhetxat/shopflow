// File này cấu hình Serilog JSON tập trung cho các API ShopFlow.
// Layer BuildingBlocks thiết lập service name, log context và TraceId/SpanId mà không ghi log ra file.
// Day 65 dùng extension này cho Order, Inventory, Payment và Notification API; Gateway giữ logger mặc định.

using Microsoft.AspNetCore.Builder;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Extension cấu hình structured logging JSON dùng chung cho các API ShopFlow.
/// </summary>
public static class SerilogLoggingExtensions
{
    /// <summary>
    /// Cấu hình một dòng JSON cho mỗi event log cùng service name và định danh trace khi có Activity.
    /// </summary>
    /// <param name="builder">Builder của API đang được khởi tạo.</param>
    /// <param name="serviceName">Tên service được ghi vào property Service.</param>
    /// <returns>Builder sau khi đã cấu hình Serilog.</returns>
    public static WebApplicationBuilder AddShopFlowSerilog(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Host.UseSerilog((_, configuration) => configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.WithProperty("Service", serviceName)
            .Enrich.With<TraceIdEnricher>()
            .Enrich.FromLogContext()
            .WriteTo.Console(new JsonFormatter()));

        return builder;
    }
}

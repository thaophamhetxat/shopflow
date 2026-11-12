// File này chứa ActivitySource chung cho toàn bộ ShopFlow và helper log scope để nối log với trace trong Jaeger.
// Layer BuildingBlocks dùng source duy nhất cho mọi service để tránh xung đột tên span khi trace lan truyền qua ASP.NET Core và HttpClient.
// Day 64 khai báo 6 span nghiệp vụ đều dùng `ActivitySource` chung này và sẽ được Serilog enrichment thay thế ở Day 65.

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// ActivitySource chung cho cả hệ thống ShopFlow, tập hợp các span doanh nghiệp cần trace ở Day 64.
/// Mọi service dùng đúng source này để Jaeger có thể gom các span trong cùng cây trace và tìm log liên quan qua TraceId/SpanId.
/// </summary>
public static class ShopFlowActivitySource
{
    public static readonly ActivitySource Source = new("ShopFlow");

    public const string OrderPlace = "order.place";
    public const string SagaStepReserve = "saga.step.reserve";
    public const string SagaStepPayment = "saga.step.payment";
    public const string OutboxProcess = "outbox.process";
    public const string InventoryReserve = "inventory.reserve";
    public const string PaymentProcess = "payment.process";

    /// <summary>
    /// Tạo scope log với TraceId và SpanId hiện tại để nối log console/console app với trace Jaeger.
    /// Cách này là giải pháp tạm cho Day 64; Serilog enrichment sẽ thay thế ở Day 65 khi có logging stack chuyên biệt.
    /// </summary>
    /// <param name="logger">Logger đang chạy ở phương thức có span đang active.</param>
    /// <returns>Scope log nếu có Activity hiện tại, ngược lại null.</returns>
    public static IDisposable? BeginLogScope(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var activity = Activity.Current;
        if (activity is null)
        {
            return null;
        }

        return logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = activity.TraceId.ToHexString(),
            ["SpanId"] = activity.SpanId.ToHexString()
        });
    }
}

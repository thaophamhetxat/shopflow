// File này chứa ActivitySource chung cho toàn bộ ShopFlow để trace lan truyền qua ASP.NET Core và HttpClient.
// Layer BuildingBlocks dùng một source duy nhất để các service xuất span vào cùng hệ thống Jaeger.
// Day 64 khai báo 6 span nghiệp vụ; Day 65 chuyển TraceId/SpanId log enrichment sang Serilog.

using System.Diagnostics;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// ActivitySource chung cho cả hệ thống ShopFlow, tập hợp các span doanh nghiệp cần trace ở Day 64.
/// Mọi service dùng đúng source này để Jaeger có thể gom các span trong cùng cây trace và tìm log liên quan qua TraceId/SpanId.
/// </summary>
public static class ShopFlowActivitySource
{
    /// <summary>Nguồn Activity chung để liên kết span nghiệp vụ ShopFlow.</summary>
    public static readonly ActivitySource Source = new("ShopFlow");

    /// <summary>Tên span tạo order.</summary>
    public const string OrderPlace = "order.place";
    /// <summary>Tên span bước reserve của saga.</summary>
    public const string SagaStepReserve = "saga.step.reserve";
    /// <summary>Tên span bước payment của saga.</summary>
    public const string SagaStepPayment = "saga.step.payment";
    /// <summary>Tên span xử lý outbox.</summary>
    public const string OutboxProcess = "outbox.process";
    /// <summary>Tên span reserve inventory.</summary>
    public const string InventoryReserve = "inventory.reserve";
    /// <summary>Tên span process payment.</summary>
    public const string PaymentProcess = "payment.process";
}

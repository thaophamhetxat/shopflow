// File này khai báo các counter nghiệp vụ dùng chung trong mọi process ShopFlow.
// Layer BuildingBlocks tránh tạo metric trùng lặp ở từng handler hoặc saga.
// Day 65 tăng OrdersCreated sau persist thành công và SagaRuns tại các kết quả cuối đã chốt.

using Prometheus;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Bộ đếm nghiệp vụ ShopFlow được chia sẻ giữa các application handler.
/// </summary>
public static class ShopFlowMetrics
{
    /// <summary>
    /// Đếm order đã được lưu thành công vào repository.
    /// </summary>
    public static readonly Counter OrdersCreated =
        Metrics.CreateCounter("shopflow_orders_created_total", "Total number of orders created.");

    /// <summary>
    /// Đếm các lần saga kết thúc theo kết quả nghiệp vụ.
    /// </summary>
    public static readonly Counter SagaRuns =
        Metrics.CreateCounter(
            "shopflow_saga_runs_total",
            "Total number of saga runs by outcome.",
            new CounterConfiguration { LabelNames = new[] { "outcome" } });

    static ShopFlowMetrics()
    {
        // Publish all allowed outcomes at zero so every scrape exposes the same metric shape before the first saga finishes.
        SagaRuns.WithLabels("completed");
        SagaRuns.WithLabels("failed_inventory");
        SagaRuns.WithLabels("failed_payment");
    }
}

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Dữ liệu snapshot dùng để tối ưu hiệu năng replay: chụp trạng thái aggregate đặt ở version nhất định.
/// Snapshot không phải source of truth; event stream mới là nguồn dữ liệu chuẩn và sẽ luôn được dùng khi replay đầy đủ.
/// </summary>
public sealed class OrderSnapshot
{
    /// <summary>
    /// Mã order được snapshot.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Version của stream tại thời điểm snapshot được tạo.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// JSON chứa trạng thái aggregate đầy đủ ở version này để reload nhanh hơn.
    /// </summary>
    public string SnapshotData { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm snapshot được lưu.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

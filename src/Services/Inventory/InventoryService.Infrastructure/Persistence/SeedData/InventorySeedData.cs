// File này chứa dữ liệu seed cố định cho năm Product của InventoryService.
// Tạo ở Day 48 để migration và smoke test luôn dùng cùng các ID, tên, giá và tồn kho.
// Gateway Day 49 và Saga Day 52 sẽ tra cứu các sản phẩm này qua Inventory API.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;

namespace InventoryService.Infrastructure.Persistence.SeedData;

/// <summary>
/// Cung cấp ID cố định và aggregate khởi tạo cho năm sản phẩm mẫu.
/// </summary>
public static class InventorySeedData
{
    /// <summary>ID cố định của Kiếm Sắt.</summary>
    public static readonly Guid IronSwordId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d101");

    /// <summary>ID cố định của Áo Giáp Da.</summary>
    public static readonly Guid LeatherArmorId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d102");

    /// <summary>ID cố định của Thuốc Hồi Phục.</summary>
    public static readonly Guid HealingPotionId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d103");

    /// <summary>ID cố định của Bùa Hộ Mệnh.</summary>
    public static readonly Guid GuardianCharmId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d104");

    /// <summary>ID cố định của Nhẫn Trữ Vật.</summary>
    public static readonly Guid StorageRingId = Guid.Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d105");

    /// <summary>
    /// Tạo năm Product hợp lệ với ID ổn định để EF Core đưa vào HasData.
    /// </summary>
    /// <returns>Danh sách năm sản phẩm mẫu, tất cả dùng VND.</returns>
    public static IReadOnlyList<Product> GetSeedProducts()
    {
        return
        [
            Product.Create(ProductId.From(IronSwordId), "Kiếm Sắt", 250000m, "VND", 100),
            Product.Create(ProductId.From(LeatherArmorId), "Áo Giáp Da", 180000m, "VND", 50),
            Product.Create(ProductId.From(HealingPotionId), "Thuốc Hồi Phục", 50000m, "VND", 200),
            Product.Create(ProductId.From(GuardianCharmId), "Bùa Hộ Mệnh", 120000m, "VND", 80),
            Product.Create(ProductId.From(StorageRingId), "Nhẫn Trữ Vật", 500000m, "VND", 20)
        ];
    }
}

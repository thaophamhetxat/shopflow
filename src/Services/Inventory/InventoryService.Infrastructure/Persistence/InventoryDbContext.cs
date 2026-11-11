// File này cấu hình EF Core cho aggregate Product và seed dữ liệu InventoryService.
// Tạo ở Day 48 để migration tạo bảng products trên database shopflow_inventory.
// Gateway Day 49 và Saga Day 52 sẽ đọc/ghi kho qua repository dựa trên DbContext này.

using InventoryService.Domain.Aggregates;
using InventoryService.Domain.ValueObjects;
using InventoryService.Infrastructure.Persistence.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InventoryService.Infrastructure.Persistence;

/// <summary>
/// EF Core context lưu Product và các sản phẩm seed của InventoryService.
/// </summary>
public sealed class InventoryDbContext : DbContext
{
    /// <summary>
    /// Tạo context với cấu hình PostgreSQL do dependency injection hoặc factory cung cấp.
    /// </summary>
    /// <param name="options">Tùy chọn EF Core cho InventoryDbContext.</param>
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options)
        : base(options)
    {
    }

    /// <summary>Tập Product được EF Core theo dõi trong database inventory.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Ánh xạ ProductId sang uuid và khai báo schema/seed ổn định cho products.
    /// </summary>
    /// <param name="modelBuilder">Bộ dựng mô hình EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var product = modelBuilder.Entity<Product>();
        product.ToTable("products");
        product.HasKey(item => item.Id);
        product.Property(item => item.Id)
            .HasConversion(new ValueConverter<ProductId, Guid>(id => id.Value, value => ProductId.From(value)))
            .HasColumnName("id")
            .ValueGeneratedNever();
        product.Property(item => item.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        product.Property(item => item.PriceAmount)
            .HasColumnName("price_amount")
            .HasPrecision(18, 2);
        product.Property(item => item.PriceCurrency)
            .HasColumnName("price_currency")
            .HasMaxLength(3)
            .IsRequired();
        product.Property(item => item.StockQuantity)
            .HasColumnName("stock_quantity")
            .IsRequired();

        // Dùng cùng ID cố định ở mọi lần dựng model để migration không sinh seed mới khi chạy lại.
        product.HasData(InventorySeedData.GetSeedProducts());
    }
}

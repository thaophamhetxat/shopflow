// File này lưu snapshot model hiện tại của InventoryDbContext để EF Core so sánh migration.
// Tạo ở Day 48 cùng bảng products và seed có ID cố định.
// Gateway Day 49 và Saga Day 52 dùng schema inventory được snapshot này xác nhận.
using System;
using InventoryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InventoryService.Infrastructure.Migrations
{
    [DbContext(typeof(InventoryDbContext))]
    /// <summary>Snapshot model Product hiện tại của InventoryService.</summary>
    partial class InventoryDbContextModelSnapshot : ModelSnapshot
    {
        /// <summary>Ghi các entity và cấu hình hiện tại vào model snapshot.</summary>
        /// <param name="modelBuilder">Bộ dựng model EF Core.</param>
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("InventoryService.Domain.Aggregates.Product", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("character varying(200)")
                        .HasColumnName("name");

                    b.Property<decimal>("PriceAmount")
                        .HasPrecision(18, 2)
                        .HasColumnType("numeric(18,2)")
                        .HasColumnName("price_amount");

                    b.Property<string>("PriceCurrency")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("character varying(3)")
                        .HasColumnName("price_currency");

                    b.Property<int>("StockQuantity")
                        .HasColumnType("integer")
                        .HasColumnName("stock_quantity");

                    b.HasKey("Id");

                    b.ToTable("products", (string)null);

                    b.HasData(
                        new
                        {
                            Id = new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d101"),
                            Name = "Kiếm Sắt",
                            PriceAmount = 250000m,
                            PriceCurrency = "VND",
                            StockQuantity = 100
                        },
                        new
                        {
                            Id = new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d102"),
                            Name = "Áo Giáp Da",
                            PriceAmount = 180000m,
                            PriceCurrency = "VND",
                            StockQuantity = 50
                        },
                        new
                        {
                            Id = new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d103"),
                            Name = "Thuốc Hồi Phục",
                            PriceAmount = 50000m,
                            PriceCurrency = "VND",
                            StockQuantity = 200
                        },
                        new
                        {
                            Id = new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d104"),
                            Name = "Bùa Hộ Mệnh",
                            PriceAmount = 120000m,
                            PriceCurrency = "VND",
                            StockQuantity = 80
                        },
                        new
                        {
                            Id = new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d105"),
                            Name = "Nhẫn Trữ Vật",
                            PriceAmount = 500000m,
                            PriceCurrency = "VND",
                            StockQuantity = 20
                        });
                });
#pragma warning restore 612, 618
        }
    }
}

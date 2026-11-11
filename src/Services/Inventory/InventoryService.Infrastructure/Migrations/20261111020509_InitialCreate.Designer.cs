// File này lưu metadata model của migration InitialCreate cho InventoryService.
// Tạo ở Day 48 bởi EF Core để mô tả schema products và năm bản ghi seed cố định.
// Gateway Day 49 và Saga Day 52 sẽ dùng schema được mô tả trong migration này.
using System;
using InventoryService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InventoryService.Infrastructure.Migrations
{
    /// <summary>Metadata thiết kế của migration Inventory InitialCreate.</summary>
    [DbContext(typeof(InventoryDbContext))]
    [Migration("20261111020509_InitialCreate")]
    partial class InitialCreate
    {
        /// <summary>Biểu diễn model Product tại thời điểm tạo migration.</summary>
        /// <param name="modelBuilder">Bộ dựng model EF Core.</param>
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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

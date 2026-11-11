// File này tạo bảng products và seed năm sản phẩm inventory trong migration InitialCreate.
// Tạo ở Day 48 để database shopflow_inventory sẵn sàng cho các thao tác Reserve/Release.
// Gateway Day 49 và Saga Day 52 sẽ dùng schema này khi gọi Inventory API.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // EF Core sinh seed dưới dạng mảng hai chiều.

namespace InventoryService.Infrastructure.Migrations
{
    /// <summary>Migration tạo bảng products và chèn năm sản phẩm seed cố định.</summary>
    public partial class InitialCreate : Migration
    {
        /// <summary>Tạo bảng products cùng các bản ghi seed VND của InventoryService.</summary>
        /// <param name="migrationBuilder">Bộ dựng các thao tác schema và dữ liệu.</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    stock_quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "products",
                columns: new[] { "id", "name", "price_amount", "price_currency", "stock_quantity" },
                values: new object[,]
                {
                    { new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d101"), "Kiếm Sắt", 250000m, "VND", 100 },
                    { new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d102"), "Áo Giáp Da", 180000m, "VND", 50 },
                    { new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d103"), "Thuốc Hồi Phục", 50000m, "VND", 200 },
                    { new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d104"), "Bùa Hộ Mệnh", 120000m, "VND", 80 },
                    { new Guid("a73b5e21-4c0f-4f91-8a61-2e5c9501d105"), "Nhẫn Trữ Vật", 500000m, "VND", 20 }
                });
        }

        /// <summary>Xóa bảng products nếu migration được hoàn tác.</summary>
        /// <param name="migrationBuilder">Bộ dựng các thao tác schema.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");
        }
    }
}

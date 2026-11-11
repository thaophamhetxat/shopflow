// File này tạo bảng payments trong migration InitialCreate của PaymentService.
// Tạo ở Day 48 để database shopflow_payment lưu được trạng thái Charge/Refund.
// Gateway Day 49 và Saga Day 52 sẽ dùng schema này khi gọi Payment API.
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <summary>Migration tạo bảng payments, không chèn khoản thanh toán mẫu.</summary>
    public partial class InitialCreate : Migration
    {
        /// <summary>Tạo bảng payments với trạng thái lưu dưới dạng chuỗi.</summary>
        /// <param name="migrationBuilder">Bộ dựng các thao tác schema.</param>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                });
        }

        /// <summary>Xóa bảng payments nếu migration được hoàn tác.</summary>
        /// <param name="migrationBuilder">Bộ dựng các thao tác schema.</param>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}

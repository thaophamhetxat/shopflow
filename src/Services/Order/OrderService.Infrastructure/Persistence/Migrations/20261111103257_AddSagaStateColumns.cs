using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Migration này bổ sung 2 cột thiếu cho `saga_state` để lưu lý do lỗi và thời điểm tạo saga row.
    /// Việc này cần thiết vì Day 52 fix 2 lưu `ErrorMessage` và `CreatedAt` trong entity, nhưng bảng cũ chỉ có order_id, state, updated_at.
    /// </summary>
    public partial class AddSagaStateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "saga_state",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "saga_state",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "saga_state");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "saga_state");
        }
    }
}

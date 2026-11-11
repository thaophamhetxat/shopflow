// File này lưu snapshot model hiện tại của PaymentDbContext để EF Core so sánh migration.
// Tạo ở Day 48 cùng bảng payments và không seed dữ liệu thanh toán.
// Gateway Day 49 và Saga Day 52 dùng schema payment được snapshot này xác nhận.
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using PaymentService.Infrastructure.Persistence;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    [DbContext(typeof(PaymentDbContext))]
    /// <summary>Snapshot model Payment hiện tại của PaymentService.</summary>
    partial class PaymentDbContextModelSnapshot : ModelSnapshot
    {
        /// <summary>Ghi entity và cấu hình hiện tại vào model snapshot.</summary>
        /// <param name="modelBuilder">Bộ dựng model EF Core.</param>
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.11")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("PaymentService.Domain.Aggregates.Payment", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<decimal>("Amount")
                        .HasPrecision(18, 2)
                        .HasColumnType("numeric(18,2)")
                        .HasColumnName("amount");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasMaxLength(3)
                        .HasColumnType("character varying(3)")
                        .HasColumnName("currency");

                    b.Property<string>("FailureReason")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)")
                        .HasColumnName("failure_reason");

                    b.Property<Guid>("OrderId")
                        .HasColumnType("uuid")
                        .HasColumnName("order_id");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("status");

                    b.HasKey("Id");

                    b.ToTable("payments", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}

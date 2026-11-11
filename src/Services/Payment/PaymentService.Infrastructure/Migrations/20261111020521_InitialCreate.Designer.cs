// File này lưu metadata model của migration InitialCreate cho PaymentService.
// Tạo ở Day 48 bởi EF Core để mô tả schema payments, không có dữ liệu seed.
// Gateway Day 49 và Saga Day 52 sẽ dùng schema được mô tả trong migration này.
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using PaymentService.Infrastructure.Persistence;

#nullable disable

namespace PaymentService.Infrastructure.Migrations
{
    /// <summary>Metadata thiết kế của migration Payment InitialCreate.</summary>
    [DbContext(typeof(PaymentDbContext))]
    [Migration("20261111020521_InitialCreate")]
    partial class InitialCreate
    {
        /// <summary>Biểu diễn model Payment tại thời điểm tạo migration.</summary>
        /// <param name="modelBuilder">Bộ dựng model EF Core.</param>
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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

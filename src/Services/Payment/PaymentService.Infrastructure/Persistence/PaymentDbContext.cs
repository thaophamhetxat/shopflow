// File này cấu hình EF Core cho aggregate Payment của PaymentService.
// Tạo ở Day 48 để migration tạo bảng payments trên database shopflow_payment, không seed khoản thanh toán.
// Gateway Day 49 và Saga Day 52 sẽ đọc/ghi trạng thái Charge/Refund qua DbContext này.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PaymentService.Domain.Aggregates;
using PaymentService.Domain.ValueObjects;

namespace PaymentService.Infrastructure.Persistence;

/// <summary>
/// EF Core context lưu Payment trong database riêng của PaymentService.
/// </summary>
public sealed class PaymentDbContext : DbContext
{
    /// <summary>
    /// Tạo context với cấu hình PostgreSQL do dependency injection hoặc factory cung cấp.
    /// </summary>
    /// <param name="options">Tùy chọn EF Core cho PaymentDbContext.</param>
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options)
        : base(options)
    {
    }

    /// <summary>Tập Payment được EF Core theo dõi trong database payment.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Ánh xạ PaymentId, trạng thái và các trường tiền tệ vào bảng payments.
    /// </summary>
    /// <param name="modelBuilder">Bộ dựng mô hình EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var payment = modelBuilder.Entity<Payment>();
        payment.ToTable("payments");
        payment.HasKey(item => item.Id);
        payment.Property(item => item.Id)
            .HasConversion(new ValueConverter<PaymentId, Guid>(id => id.Value, value => PaymentId.From(value)))
            .HasColumnName("id")
            .ValueGeneratedNever();
        payment.Property(item => item.OrderId)
            .HasColumnName("order_id")
            .IsRequired();
        payment.Property(item => item.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();
        payment.Property(item => item.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();
        payment.Property(item => item.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();
        payment.Property(item => item.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(500)
            .IsRequired(false);
    }
}

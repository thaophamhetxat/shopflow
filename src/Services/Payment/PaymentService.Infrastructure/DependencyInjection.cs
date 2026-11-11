// File này đăng ký DbContext, repository và mock gateway cho PaymentService.Infrastructure.
// Tạo ở Day 48 để API dùng PostgreSQL payment riêng và gateway giả lập có log.
// Gateway Day 49 sẽ thay mock; Saga Day 52 vẫn nhận abstraction IPaymentGateway.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Repositories;
using PaymentService.Infrastructure.Gateways;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Persistence.Repositories;

namespace PaymentService.Infrastructure;

/// <summary>
/// Đăng ký các thành phần persistence và gateway của PaymentService.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Đăng ký PostgreSQL DbContext, repository scoped và mock gateway singleton.
    /// </summary>
    /// <param name="services">Container dịch vụ cần bổ sung đăng ký.</param>
    /// <param name="configuration">Cấu hình ứng dụng chứa ConnectionStrings:PaymentDb.</param>
    /// <returns>Container sau khi đã đăng ký Infrastructure.</returns>
    /// <exception cref="InvalidOperationException">Ném nếu connection string PaymentDb bị thiếu.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("PaymentDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Thiếu connection string 'PaymentDb' trong cấu hình.");
        }

        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        // Gateway singleton an toàn vì implementation mặc định dùng Random.Shared và không giữ trạng thái thanh toán.
        services.AddSingleton<IPaymentGateway, MockPaymentGateway>();

        return services;
    }
}

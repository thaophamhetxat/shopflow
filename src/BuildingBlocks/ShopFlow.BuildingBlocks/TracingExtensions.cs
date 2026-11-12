// File này chứa extension tập trung cho OpenTelemetry tracing, dùng đúng 1 ActivitySource chung của ShopFlow.
// Layer BuildingBlocks đăng ký resource service.name and instrumentation cho ASP.NET Core, HttpClient và OTLP exporter.
// Day 64 gộp setup tracing ở đây để 3 API không tự tạo OTel lẻ tẻ trong từng Program.cs.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ShopFlow.BuildingBlocks;

/// <summary>
/// Extension dùng để đăng ký OpenTelemetry tracing cho từng service ShopFlow với cùng ActivitySource `ShopFlow`.
/// Service name và OTLP endpoint được lấy từ config, giúp mọi service chạy cùng một cấu hình `Tracing:OtlpEndpoint`.
/// </summary>
public static class TracingExtensions
{
    /// <summary>
    /// Đăng ký pipeline tracing của OpenTelemetry với AspNetCore, HttpClient, OTLP exporter và source `ShopFlow`.
    /// </summary>
    /// <param name="services">Container DI của service đang chạy.</param>
    /// <param name="configuration">Cấu hình application; đọc `Tracing:OtlpEndpoint` nếu có.</param>
    /// <param name="serviceName">Tên service phân biệt: `shopflow-order`, `shopflow-inventory`, `shopflow-payment`.</param>
    /// <returns>Container DI sau khi đăng ký tracing.</returns>
    public static IServiceCollection AddShopFlowTracing(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                    .AddSource(ShopFlowActivitySource.Source.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(configuration["Tracing:OtlpEndpoint"] ?? "http://localhost:4317");
                    });
            });

        return services;
    }
}

// File này khởi tạo Payment API, đăng ký use case và công bố endpoint/health/Swagger.
// Cập nhật ở Day 48 để Charge, Refund và đọc thanh toán chạy qua MediatR và PostgreSQL.
// Gateway Day 49 sẽ proxy các route này; Saga Day 52 sẽ gọi Charge/Refund để điều phối thanh toán.
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using PaymentService.API.Endpoints;
using PaymentService.Application;
using PaymentService.Infrastructure;
using ShopFlow.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);
builder.AddShopFlowSerilog("shopflow-payment");

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddShopFlowHealthChecks(
    serviceProvider => serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString("PaymentDb"),
    null,
    null);
builder.Services.AddShopFlowTracing(builder.Configuration, "shopflow-payment");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShopFlow Payment API v1", Version = "v1" });
});

var app = builder.Build();

app.UseShopFlowMetrics();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopFlow Payment API v1");
});

app.MapHealthChecks("/health");
// Readiness kiểm tra database payment vì các thao tác nghiệp vụ cần persistence sẵn sàng.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapPaymentEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

app.Run();
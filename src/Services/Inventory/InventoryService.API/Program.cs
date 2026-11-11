// File này khởi tạo Inventory API, đăng ký use case và công bố endpoint/health/Swagger.
// Cập nhật ở Day 48 để Reserve, Release và đọc tồn kho chạy qua MediatR và PostgreSQL.
// Gateway Day 49 sẽ proxy các route này; Saga Day 52 sẽ gọi Reserve/Release để điều phối kho.
using InventoryService.API.Endpoints;
using InventoryService.Application;
using InventoryService.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShopFlow Inventory API v1", Version = "v1" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopFlow Inventory API v1");
});

app.MapHealthChecks("/health");
app.MapInventoryEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

app.Run();
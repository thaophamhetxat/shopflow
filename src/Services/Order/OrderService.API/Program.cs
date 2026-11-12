// File này chứa entry point của OrderService.API, nơi đăng ký dependency và cấu hình Swagger, endpoint v1/v2 và health probe.
// Tạo ở Day 47 để API có thể nhận request thật cho Order, đồng thời mở danh mục OpenAPI để demo versioning và tương thích v1-v2.
// Gateway Day 49 sẽ proxy qua các route này, nên API hiện tại phải là contract rõ ràng và có Problem Details chuẩn RFC 9457.
using Microsoft.OpenApi.Models;
using OrderService.API.Endpoints;
using OrderService.Application;
using OrderService.Infrastructure;
using ShopFlow.BuildingBlocks;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký health-check services: MapHealthChecks("/health") ở dưới KHÔNG tự đăng ký service, thiếu dòng này thì app ném InvalidOperationException và chết ngay lúc khởi động (lỗi từ Day 47, Day 50 phát hiện ra).
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddShopFlowTracing(builder.Configuration, "shopflow-order");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "ShopFlow Order API v1", Version = "v1" });
    options.SwaggerDoc("v2", new OpenApiInfo { Title = "ShopFlow Order API v2", Version = "v2" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShopFlow Order API v1");
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "ShopFlow Order API v2");
});

app.MapHealthChecks("/health");
app.MapOrderEndpoints();
app.MapGet("/health/live", () => Results.Ok(new { status = "Live" }));

app.Run();
public partial class Program { }
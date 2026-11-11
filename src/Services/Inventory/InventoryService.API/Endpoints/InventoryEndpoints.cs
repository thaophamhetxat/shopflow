// File này ánh xạ các route HTTP của Inventory API vào command/query MediatR.
// Tạo ở Day 48 để cung cấp Reserve, Release và xem tồn kho kèm Problem Details nhất quán.
// Gateway Day 49 sẽ proxy các route này; Saga Day 52 sẽ gọi Reserve/Release qua HTTP.

using InventoryService.API.Dtos;
using InventoryService.Application;
using InventoryService.Application.Commands;
using InventoryService.Application.Queries;
using MediatR;

namespace InventoryService.API.Endpoints;

/// <summary>
/// Đăng ký các endpoint riêng của InventoryService.
/// </summary>
public static class InventoryEndpoints
{
    /// <summary>
    /// Thêm POST Reserve, POST Release và GET Product vào WebApplication.
    /// </summary>
    /// <param name="app">Ứng dụng ASP.NET Core cần ánh xạ route.</param>
    /// <returns>Ứng dụng sau khi các route được đăng ký.</returns>
    public static WebApplication MapInventoryEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/inventory/reserve", async (
            ReserveStockRequest request,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InventoryEndpoints");
            try
            {
                var result = await sender.Send(new ReserveStockCommand(request.ProductId, request.Quantity), context.RequestAborted);
                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("ReserveInventory").WithTags("Inventory");

        app.MapPost("/inventory/release", async (
            ReleaseStockRequest request,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InventoryEndpoints");
            try
            {
                var result = await sender.Send(new ReleaseStockCommand(request.ProductId, request.Quantity), context.RequestAborted);
                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("ReleaseInventory").WithTags("Inventory");

        app.MapGet("/inventory/{id:guid}", async (
            Guid id,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("InventoryEndpoints");
            try
            {
                var result = await sender.Send(new GetProductQuery(id), context.RequestAborted);
                if (result is null)
                {
                    throw new ProductNotFoundException(id);
                }

                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("GetInventoryProduct").WithTags("Inventory");

        return app;
    }

    /// <summary>
    /// Ghi log lỗi và trả Problem Details với nội dung đã được chuẩn hóa.
    /// </summary>
    /// <param name="exception">Lỗi phát sinh trong endpoint.</param>
    /// <param name="context">HTTP context dùng để đặt đường dẫn instance.</param>
    /// <param name="logger">Logger cho lỗi request.</param>
    /// <returns>Problem Details chứa status/title/detail đã ánh xạ.</returns>
    private static IResult ToProblem(Exception exception, HttpContext context, ILogger logger)
    {
        var problem = InventoryProblemDetailsMapping.Map(exception);
        if (problem.StatusCode >= 500)
        {
            logger.LogError(exception, "Lỗi hệ thống khi xử lý Inventory request {Path}.", context.Request.Path);
        }
        else
        {
            logger.LogInformation(exception, "Inventory request bị từ chối tại {Path}.", context.Request.Path);
        }

        return Results.Problem(
            statusCode: problem.StatusCode,
            title: problem.Title,
            detail: problem.Detail,
            instance: context.Request.Path);
    }
}

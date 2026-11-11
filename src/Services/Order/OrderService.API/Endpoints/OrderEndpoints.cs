// File này chứa route group cho OrderService.API, gắn logic v1/v2 vào Endpoint minimal API và map exception sang Problem Details chuẩn.
// Tạo ở Day 47 để client gọi POST/GET/DELETE /api/v1/orders và GET /api/v2/orders/{id} theo contract đã được chốt.
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using OrderService.API.Dtos;
using OrderService.Application;
using OrderService.Application.Commands;
using OrderService.Application.Queries;
using OrderService.Domain.Exceptions;
using OrderService.Domain.ValueObjects;

namespace OrderService.API.Endpoints;

/// <summary>
/// Endpoint API cho OrderService, gồm route v1 và v2; mỗi route đều catch exception và trả Problem Details theo RFC 9457.
/// V2 chỉ thêm EstimatedDelivery trên GET chi tiết, còn POST/DELETE vẫn đi qua v1 như quy định Day 47 để tránh phát sinh contract mới không cần thiết.
/// </summary>
public static class OrderEndpoints
{
    /// <summary>
    /// Đăng ký các route của OrderService API theo contract v1 và v2, đồng thời cấu hình lộ trình lỗi tập trung và problem details.
    /// </summary>
    /// <param name="app">WebApplication đang chạy.</param>
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");
        var v2 = app.MapGroup("/api/v2");

        // POST /api/v1/orders: tạo đơn qua PlaceOrderCommand, trả 201 + Location vì tài nguyên mới được tạo ở /api/v1/orders/{id}.
        v1.MapPost("/orders", async (HttpContext httpContext, IMediator mediator, PlaceOrderRequest request, CancellationToken cancellationToken) =>
        {
            try
            {
                var idempotencyKey = httpContext.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
                var command = new PlaceOrderCommand(
                    request.CustomerId,
                    request.Street,
                    request.City,
                    request.Country,
                    request.PostalCode,
                    request.Items.Select(item => new PlaceOrderItem(
                        item.ProductId,
                        item.Quantity,
                        item.UnitPriceAmount,
                        item.UnitPriceCurrency)).ToList(),
                    idempotencyKey);

                var orderId = await mediator.Send(command, cancellationToken);
                return Results.Created($"/api/v1/orders/{orderId.Value}", new { orderId = orderId.Value });
            }
            catch (Exception exception)
            {
                return MapExceptionToResult(httpContext, exception);
            }
        });

        // GET /api/v1/orders/{id}: đọc chi tiết đơn bằng GetOrderByIdQuery để trả payload v1 đầy đủ mà không thêm field mới.
        v1.MapGet("/orders/{id:guid}", async (HttpContext httpContext, IMediator mediator, Guid id, CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await mediator.Send(new GetOrderByIdQuery(id), cancellationToken);
                if (detail is null)
                {
                    throw new OrderNotFoundException(id);
                }

                return Results.Ok(detail);
            }
            catch (Exception exception)
            {
                return MapExceptionToResult(httpContext, exception);
            }
        });

        // GET /api/v1/orders: list theo customerId với cursor và pageSize, trả 200 khi hợp lệ, 422 khi input không parse được hoặc thiếu tham số bắt buộc.
        v1.MapGet("/orders", async (HttpContext httpContext, IMediator mediator, CancellationToken cancellationToken) =>
        {
            try
            {
                var query = httpContext.Request.Query;
                var customerIdRaw = query["customerId"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(customerIdRaw) || !Guid.TryParse(customerIdRaw, out var customerId))
                {
                    throw new ValidationException(new[]
                    {
                        new FluentValidation.Results.ValidationFailure("customerId", "customerId không hợp lệ hoặc bị thiếu.")
                    });
                }

                Guid? cursorOrderId = null;
                var cursorRaw = query["cursor"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(cursorRaw) && !Guid.TryParse(cursorRaw, out var parsedCursor))
                {
                    throw new ValidationException(new[]
                    {
                        new FluentValidation.Results.ValidationFailure("cursor", "cursor không hợp lệ.")
                    });
                }

                if (Guid.TryParse(cursorRaw, out var validCursor))
                {
                    cursorOrderId = validCursor;
                }

                var pageSize = 20;
                var pageSizeRaw = query["pageSize"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(pageSizeRaw) && !int.TryParse(pageSizeRaw, out pageSize))
                {
                    throw new ValidationException(new[]
                    {
                        new FluentValidation.Results.ValidationFailure("pageSize", "pageSize phải là số nguyên hợp lệ.")
                    });
                }

                var result = await mediator.Send(new ListOrdersByCustomerQuery(customerId, cursorOrderId, pageSize), cancellationToken);
                return Results.Ok(result);
            }
            catch (Exception exception)
            {
                return MapExceptionToResult(httpContext, exception);
            }
        });

        // DELETE /api/v1/orders/{id}: hủy đơn theo CancelOrderCommand, không phải xóa cứng khỏi DB; đơn chuyển sang Cancelled và vẫn còn để tra cứu.
        v1.MapDelete("/orders/{id:guid}", async (HttpContext httpContext, IMediator mediator, Guid id, CancellationToken cancellationToken) =>
        {
            try
            {
                await mediator.Send(new CancelOrderCommand(id), cancellationToken);
                return Results.NoContent();
            }
            catch (Exception exception)
            {
                return MapExceptionToResult(httpContext, exception);
            }
        });

        // GET /api/v2/orders/{id}: cùng query v1 nhưng trả dữ liệu v2 với EstimatedDelivery thêm vào để demo contract versioning không làm v1 bị phá vỡ.
        v2.MapGet("/orders/{id:guid}", async (HttpContext httpContext, IMediator mediator, Guid id, CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await mediator.Send(new GetOrderByIdQuery(id), cancellationToken);
                if (detail is null)
                {
                    throw new OrderNotFoundException(id);
                }

                var v2 = OrderDetailV2Dto.From(detail);
                return Results.Ok(v2);
            }
            catch (Exception exception)
            {
                return MapExceptionToResult(httpContext, exception);
            }
        });
    }

    private static IResult MapExceptionToResult(HttpContext httpContext, Exception exception)
    {
        var mapped = ProblemDetailsMapping.Map(exception, httpContext.Request.Path.Value ?? "/");
        return Results.Problem(
            detail: mapped.Detail,
            statusCode: mapped.StatusCode,
            title: mapped.Title,
            type: $"https://shopflow.local/errors/{mapped.StatusCode}",
            instance: httpContext.Request.Path.Value ?? "/");
    }
}

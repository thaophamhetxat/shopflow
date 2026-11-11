// File này ánh xạ các route HTTP của Payment API vào command/query MediatR.
// Tạo ở Day 48 để cung cấp Charge, Refund và tra cứu Payment kèm Problem Details.
// Gateway Day 49 sẽ proxy các route này; Saga Day 52 sẽ gọi Charge/Refund để điều phối.

using MediatR;
using PaymentService.API.Dtos;
using PaymentService.Application;
using PaymentService.Application.Commands;
using PaymentService.Application.Queries;

namespace PaymentService.API.Endpoints;

/// <summary>
/// Đăng ký các endpoint riêng của PaymentService.
/// </summary>
public static class PaymentEndpoints
{
    /// <summary>
    /// Thêm POST thanh toán, GET Payment và POST Refund vào WebApplication.
    /// </summary>
    /// <param name="app">Ứng dụng ASP.NET Core cần ánh xạ route.</param>
    /// <returns>Ứng dụng sau khi các route được đăng ký.</returns>
    public static WebApplication MapPaymentEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/payments", async (
            ProcessPaymentRequest request,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");
            try
            {
                var payment = await sender.Send(
                    new ProcessPaymentCommand(request.OrderId, request.Amount, request.Currency),
                    context.RequestAborted);

                // Gateway từ chối vẫn là kết quả Payment Failed; POST luôn trả 201 vì khoản thanh toán đã được ghi nhận.
                return Results.Created($"/payments/{payment.PaymentId}", payment);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("ProcessPayment").WithTags("Payment");

        app.MapGet("/payments/{id:guid}", async (
            Guid id,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");
            try
            {
                var payment = await sender.Send(new GetPaymentQuery(id), context.RequestAborted);
                if (payment is null)
                {
                    throw new PaymentNotFoundException(id);
                }

                return Results.Ok(payment);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("GetPayment").WithTags("Payment");

        app.MapPost("/payments/{id:guid}/refund", async (
            Guid id,
            ISender sender,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");
            try
            {
                var payment = await sender.Send(new RefundPaymentCommand(id), context.RequestAborted);
                return Results.Ok(payment);
            }
            catch (Exception exception)
            {
                return ToProblem(exception, context, logger);
            }
        }).WithName("RefundPayment").WithTags("Payment");

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
        var problem = PaymentProblemDetailsMapping.Map(exception);
        if (problem.StatusCode >= 500)
        {
            logger.LogError(exception, "Lỗi hệ thống khi xử lý Payment request {Path}.", context.Request.Path);
        }
        else
        {
            logger.LogInformation(exception, "Payment request bị từ chối tại {Path}.", context.Request.Path);
        }

        return Results.Problem(
            statusCode: problem.StatusCode,
            title: problem.Title,
            detail: problem.Detail,
            instance: context.Request.Path);
    }
}

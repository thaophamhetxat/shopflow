namespace OrderService.Domain.Exceptions;

/// <summary>
/// Ngoại lệ báo hiệu xung đột optimistic concurrency khi hai writer cùng append event stream với cùng expectedVersion.
/// Đây là cơ chế bảo vệ rằng trong một stream order, không có hai transaction khác nhau cùng ghi cùng version mà không phát hiện.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message)
        : base(message)
    {
    }

    public ConcurrencyException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

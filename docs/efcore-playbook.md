# EF Core playbook cho ShopFlow

## Mục tiêu

Tài liệu này ghi lại 10 best practice EF Core đã được chứng minh trong repo ShopFlow, với mốc Day thật để team triển khai và kiểm tra khi refactor query hoặc persistence.

### 1. Project bằng `Select` trong SQL, map phần không dịch được ở memory (Day 46/60/61)

Chỉ chọn các cột cần thiết cho read model bằng `Select`/projection phía database. Một số phép biến đổi domain như Money serialize thành `amount|currency` hoặc `Guid.CompareTo` không dịch được sang SQL thì thực hiện ở memory sau khi đã giới hạn dữ liệu trong database.

### 2. Dùng keyset pagination trong SQL với `CreatedAt` và `Id` (Day 46/61)

Dùng keyset thay cho `OFFSET` để trang tiếp theo không bị trôi khi có dữ liệu mới. Lọc và sắp xếp theo `CreatedAt`; dùng `Id` làm tie-breaker ổn định, xử lý trong memory khi provider không dịch phép so sánh `Guid` của value object.

### 3. Dùng compiled query cho hot path và đo hiệu quả thực tế (Day 61)

ShopFlow dùng `EF.CompileAsyncQuery` cho stock check và order detail. EF Core vốn đã cache query compilation; compiled query không đảm bảo một mức cải thiện cố định như 30%, vì vậy cần benchmark workload thật thay vì mặc định kỳ vọng một tỷ lệ.

### 4. Dùng `AsNoTracking()` cho đường đọc, tracking khi cần ghi (Day 60)

Read model chỉ đọc nên dùng `AsNoTracking()` để không đưa entity vào `ChangeTracker`. Các command cần thay đổi aggregate dùng entity tracked để EF Core lưu đúng thay đổi khi `SaveChangesAsync` được gọi.

### 5. Dùng `SELECT ... FOR UPDATE` trong transaction Serializable cho reservation (Day 58/63)

Mở transaction tường minh ở `ProductRepository.ReserveAsync`, khóa row bằng `SELECT ... FOR UPDATE`, rồi áp dụng domain rule, lưu và commit trong cùng transaction với `IsolationLevel.Serializable`. PostgreSQL có thể trả SQLSTATE `40001` khi tranh chấp; retry có giới hạn phải tạo transaction mới và đọc lại row.

### 6. Không dùng `lock()` trong async; dùng `SemaphoreSlim`/`AsyncLock` (Day 58)

`lock()` không thể giữ đồng bộ qua `await` và không phù hợp để khóa luồng async. Dùng `SemaphoreSlim` hoặc `AsyncLock` để việc chờ lock có thể await và hỗ trợ cancellation.

### 7. Dùng `ExecuteUpdateAsync` cho bulk update thay vì N lần `SaveChangesAsync` (Day 61)

Outbox drain gom các message cần đánh dấu rồi cập nhật bằng một bulk `ExecuteUpdateAsync`. Điều này giảm số lệnh database so với lưu từng entity một, đồng thời giữ semantics xử lý at-least-once.

### 8. Cache-aside: TTL và invalidate khi ghi; không cache entity tracked (Day 62)

Redis là tối ưu hóa best-effort, không phải nguồn dữ liệu đúng duy nhất. Dùng TTL, invalidate sau các thao tác ghi, chỉ cache dữ liệu đọc cần thiết thay vì entity tracked, và dựng lại aggregate qua factory khi cần.

### 9. Tune connection pool bằng `Maximum Pool Size`/`Minimum Pool Size` (Day 62)

Điều chỉnh pool theo tải và giới hạn tài nguyên database. Cấu hình Inventory hiện đặt `Maximum Pool Size=20` và `Minimum Pool Size=5`; mọi thay đổi cần được đo và cấu hình qua connection string hiện hành.

### 10. Xác minh bằng SQL log, query count và guard latency trung bình (Day 60/61/63)

Dùng EF Core `LogTo` để xem `Executed DbCommand`, đồng thời test số query để bắt N+1 hoặc full-table load. Với hot path, đo trung bình qua nhiều lần lặp và đặt ngưỡng có căn cứ từ baseline để giảm flaky do một lần chạy bất thường.

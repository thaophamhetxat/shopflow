# EF Core playbook cho ShopFlow

## Mục tiêu

Tài liệu này ghi lại 10 best practice EF Core đã được chứng minh trong repo ShopFlow, với mốc Day thật để team triển khai và kiểm tra khi refactor query hoặc persistence.

### 1. Set transaction boundary explicitly for critical writes (Day 58)

Always open a transaction around the row-locking workflow that performs read-modify-write. In `ProductRepository.ReserveAsync`, the `SELECT ... FOR UPDATE` needs a surrounding transaction, otherwise PostgreSQL releases the lock immediately after the read and concurrent reservations can race.

### 2. Use `SELECT ... FOR UPDATE` for inventory reservation (Day 58)

When several requests reserve the same product at once, the database must serialize the hot row. The guarded query should target the specific product row and hold the lock until the reservation is saved and committed.

### 3. Apply `IsolationLevel.Serializable` on the reservation path (Day 63)

The reservation workflow is not just a single-row lock; it is also a write-skew-sensitive workflow. Using `BeginTransactionAsync(IsolationLevel.Serializable, ...)` closes the gap where two reservations read the same stock snapshot before either commits.

### 4. Prefer compiled queries for hot read paths (Day 61)

`OrderRepository.GetOrderDetailAsync` and `ProductRepository.CheckStockAsync` cache `EF.CompileAsyncQuery` delegates so the LINQ expression is not recompiled on every request. The result is a tighter hot-path latency profile while keeping query behavior unchanged.

### 5. Keep read-only queries `AsNoTracking` (Day 46)

Read models and DTO projection queries should not attach entities to the `DbContext`. This avoids accidentally mutating tracked state and reduces memory overhead for projection read paths such as detail/list queries.

### 6. Use bulk update for outbox drain (Day 61)

For the outbox, prefer one bulk `ExecuteUpdateAsync` at the end of the drain cycle instead of N individual saves. This keeps SQL count low and preserves idempotent processing semantics for the outbox pattern.

### 7. Cache-aside should be a performance optimization, not correctness (Day 62)

The Redis cache is a best-effort optimization with TTL plus invalidation on reserve/release. Database correctness remains the source of truth; the cache only reduces repeated reads and dampens hot-path burstiness.

### 8. Protect the database connection pool (Day 62)

The app should tune connection pool sizing to the workload, with a non-zero minimum pool and a bounded maximum pool. In ShopFlow, the inventory service uses `Maximum Pool Size=20` and `Minimum Pool Size=5` to avoid churn while keeping resource usage controlled.

### 9. Log SQL and verify query count in tests (Day 60)

When a change is supposed to remove N+1 or a full-table load, log the `DbCommand` output and assert the command count. This is the quickest way to catch accidental overhead before it reaches production.

### 10. Guard performance with average-based regression tests (Day 63)

Critical paths must carry latency guards that evaluate the mean of many repeated measurements. This avoids flaky single-run timing and catches real regressions such as reintroduced N+1 patterns or a stale compiled query path.

## Execution checklist

- Validate the isolation level with a real transaction log.
- Measure average latency over repeated calls, not a single-run snapshot.
- Keep the database as the source of truth; cache and query optimization are secondary.
- Save a benchmark note in `docs/performance-baseline.md` whenever a release gate changes.

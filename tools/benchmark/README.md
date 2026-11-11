# Benchmark Order API

Chạy PostgreSQL và Redis bằng `docker compose`, sau đó khởi động Order API ở cổng 5181. Từ thư mục gốc repository, chạy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\benchmark\measure-order-endpoints.ps1
```

Có thể đổi địa chỉ API bằng `-BaseUrl`. Tùy chọn `-ExecutionPolicy Bypass` chỉ áp dụng cho process PowerShell này, không thay đổi policy lưu trên máy; cần dùng trong môi trường hiện đang chặn chạy script. Script kiểm tra `/health`, chuẩn bị dữ liệu cần thiết, rồi gửi tuần tự 100 request cho mỗi endpoint Order v1/v2. Nó in và ghi kết quả thật vào `tools/benchmark/baseline-day51.txt`; request sai status được đếm riêng và không được tính vào latency.

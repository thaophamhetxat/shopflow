# Gateway YARP ShopFlow

## Route → service → port

| Route | Service | Downstream port | Mục tiêu |
| --- | --- | --- | --- |
| `/api/v1/orders/{**catch-all}` | Order API | `5181` | Tạo, đọc và hủy đơn theo contract v1. |
| `/api/v2/orders/{**catch-all}` | Order API | `5181` | Đọc chi tiết đơn theo contract v2 với `EstimatedDelivery`. |
| `/inventory/{**catch-all}` | Inventory API | `5140` | Xem tồn kho, reserve và release hàng. |
| `/payments/{**catch-all}` | Payment API | `5199` | Tạo payment, đọc payment và refund. |

## Vì sao không dùng transform đổi path

Các route phía client và downstream là đồng nhất, ví dụ `POST /api/v1/orders` gửi qua gateway vẫn phải đến Order API như `POST /api/v1/orders`. Vì vậy route YARP không cần `PathRemovePrefix`, `PathPattern` hay transform nào khác. Nếu thêm transform, path sẽ bị đổi rồi request sẽ không khớp với route thật của từng API.

## Vì sao tên cluster và destination không có dấu gạch ngang

Tên như `ordersCluster`, `inventoryCluster`, `paymentsCluster` và `orders1`, `inventory1`, `payments1` được chọn để Docker Compose override qua biến môi trường chạy trơn tru. Với tên có dấu `-`, biến môi trường `ReverseProxy__Clusters__ordersCluster__Destinations__orders1__Address` dễ bị parser/override sai khi container khởi động; cách camelCase giữ tên ổn định hơn trong `.NET` và `docker-compose.yml`.

## Chạy local

1. Khởi động database và infra: `docker compose up -d`.
2. Khởi động 3 API local: Order, Inventory và Payment theo port đã định rõ trong `launchSettings.json`.
3. Chạy gateway: `dotnet run --project src/Gateway/ShopFlow.Gateway --launch-profile http`.
4. Gọi gateway qua `http://localhost:5000` để kiểm tra health và route proxy.

## Chạy qua Docker Compose

Sau khi 3 API local đang chạy, chỉ cần `docker compose up -d shopflow-gateway` để build và start gateway theo port 5000. Gateway trong container sẽ trỏ về `host.docker.internal` thay vì `localhost` vì `localhost` trong container là chính container đó, không phải máy host.

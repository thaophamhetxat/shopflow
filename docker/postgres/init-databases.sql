-- File này tạo thêm các database tách biệt cho từng Bounded Context của ShopFlow.
-- shopflow_order là database mặc định của Order service; các database còn lại được tạo trong cùng instance Postgres.
CREATE DATABASE shopflow_inventory;
-- shopflow_inventory thuộc Bounded Context Inventory, nơi lưu kho và reserve/release status.
CREATE DATABASE shopflow_payment;
-- shopflow_payment thuộc Bounded Context Payment, nơi lưu thanh toán và compensation data.
CREATE DATABASE shopflow_notification;
-- shopflow_notification thuộc Bounded Context Notification, nơi lưu log thông báo và outbox event.
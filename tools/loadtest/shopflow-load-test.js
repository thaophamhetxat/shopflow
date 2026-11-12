// Day 66: load testing and performance tuning
// Script k6 mô phỏng tải đồng thời cho ShopFlow: 35 VU tạo đơn, 10 VU đọc chi tiết, 5 VU list theo customer.
// Mục đích là đo throughput, error rate HTTP và p99 trước/sau tuning mà không đổi luồng nghiệp vụ hay cấu trúc API.

import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:5181').trim();
const TEST_DURATION = (__ENV.TEST_DURATION || '5m').trim();
const PRODUCT_ID = 'a73b5e21-4c0f-4f91-8a61-2e5c9501d101';
const PRODUCT_PRICE = 250000;
const PRODUCT_CURRENCY = 'VND';
const ORDER_SETUP_COUNT = 20;

function buildCreateOrderBody(customerId, street = '123 Le Loi', city = 'Hanoi', country = 'Vietnam', postalCode = '100000') {
    return {
        customerId,
        street,
        city,
        country,
        postalCode,
        items: [
            {
                productId: PRODUCT_ID,
                quantity: 1,
                unitPriceAmount: PRODUCT_PRICE,
                unitPriceCurrency: PRODUCT_CURRENCY
            }
        ]
    };
}

export function setup() {
    // Setup chạy 1 lần trước khi test: tạo 20 order cùng một customerId để list_orders có dữ liệu và để detail endpoint có id hợp lệ.
    // Dùng cùng customerId cho toàn bộ setup là cần thiết vì list_orders filter theo customer; mỗi order lại phải có idempotency key mới để tránh được cache tần suất lặp lại.
    const customerId = crypto.randomUUID();
    const orderIds = [];

    for (let i = 0; i < ORDER_SETUP_COUNT; i++) {
        const body = buildCreateOrderBody(customerId);
        const res = http.post(`${BASE_URL}/api/v1/orders`, JSON.stringify(body), {
            headers: {
                'Content-Type': 'application/json',
                'X-Idempotency-Key': crypto.randomUUID()
            }
        });

        const ok = check(res, {
            'setup order created': (r) => r.status === 201,
            'setup order id returned': (r) => {
                try {
                    const payload = r.json();
                    return !!payload && !!payload.orderId;
                } catch (error) {
                    return false;
                }
            }
        });

        if (!ok) {
            throw new Error(`Setup order creation failed: status=${res.status} body=${res.body}`);
        }

        orderIds.push(res.json('orderId'));
    }

    return { customerId, orderIds };
}

export const options = {
    scenarios: {
        // place_order: ưu tiên phần lớn traffic, 35 VU, vì đây là action tạo order xảy ra trong hệ thống chính và cần phản ánh workload thực tế của đặt hàng.
        place_order: {
            executor: 'constant-vus',
            vus: 35,
            duration: TEST_DURATION,
            exec: 'placeOrder'
        },
        // get_order: đọc nhiều chi tiết đơn, 10 VU, giúp đo latency của endpoint detail khi database đang có nhiều đơn mới.
        get_order: {
            executor: 'constant-vus',
            vus: 10,
            duration: TEST_DURATION,
            exec: 'getOrder'
        },
        // list_orders: chiếm một phần nhỏ nhưng cần đánh giá query theo customer và pagination, nên dùng 5 VU để bảo toàn mix 70/20/10.
        list_orders: {
            executor: 'constant-vus',
            vus: 5,
            duration: TEST_DURATION,
            exec: 'listOrders'
        }
    },
    thresholds: {
        http_req_failed: ['rate<0.05']
    },
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(50)', 'p(99)', 'count']
};

export function placeOrder(data) {
    // Mỗi request tạo order mới với customerId mới để đo đúng nhánh PlaceOrder và tránh idempotency cache làm giảm workload thật.
    const customerId = crypto.randomUUID();
    const body = buildCreateOrderBody(customerId);
    const res = http.post(`${BASE_URL}/api/v1/orders`, JSON.stringify(body), {
        headers: {
            'Content-Type': 'application/json',
            'X-Idempotency-Key': crypto.randomUUID()
        }
    });

    check(res, {
        'place_order status is 201': (r) => r.status === 201
    });
}

export function getOrder(data) {
    // get_order lấy ngẫu nhiên từ 20 order đã setup để không phải gọi order không tồn tại; tập trung đo read detail với dữ liệu đã có trong DB.
    const orderId = data.orderIds[Math.floor(Math.random() * data.orderIds.length)];
    const res = http.get(`${BASE_URL}/api/v1/orders/${orderId}`);

    check(res, {
        'get_order status is 200': (r) => r.status === 200
    });
}

export function listOrders(data) {
    // list_orders phải filter theo cùng customerId đã tạo trong setup để lưu lại dữ liệu hiển thị và kiểm tra hiệu năng của cursor/page query.
    const res = http.get(`${BASE_URL}/api/v1/orders?customerId=${data.customerId}&pageSize=20`);

    check(res, {
        'list_orders status is 200': (r) => r.status === 200
    });
}

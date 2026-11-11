param( # Khai báo tham số đầu vào để có thể đổi địa chỉ API mà không sửa script.
    [string]$BaseUrl = "http://localhost:5181" # Dùng cổng Order API mặc định theo cấu hình Day 47.
) # Kết thúc khai báo tham số của script.

$ErrorActionPreference = "Stop" # Dừng với lỗi rõ ràng khi cấu hình hoặc chuẩn bị dữ liệu không thực hiện được.
$script:BaseUrl = $BaseUrl.TrimEnd("/") # Chuẩn hóa URL để ghép đường dẫn không bị dấu gạch chéo kép.
$requestCount = 100 # Đo đúng 100 request tuần tự cho mỗi endpoint theo phạm vi Day 51.
$expectedProductId = [Guid]::NewGuid() # Dùng một mã sản phẩm giả ổn định trong các request tạo đơn của lần chạy này.
Add-Type -AssemblyName System.Net.Http # Nạp HttpClient rõ ràng vì Windows PowerShell 5.1 không tự nạp assembly này.
$script:HttpClient = [System.Net.Http.HttpClient]::new() # Dùng HttpClient để đọc cả response thành công lẫn status lỗi.
$script:HttpClient.Timeout = [TimeSpan]::FromSeconds(30) # Giới hạn thời gian chờ để một request treo không làm kẹt cả lần đo.

function Invoke-ApiRequest { # Gửi một HTTP request đồng bộ để vòng đo xử lý tuần tự từng mẫu.
    param( # Khai báo dữ liệu cần thiết cho một lần gọi API.
        [string]$Method, # Nhận phương thức HTTP của endpoint cần gọi.
        [string]$Path, # Nhận đường dẫn tương đối để ghép với BaseUrl.
        [object]$Body = $null, # Nhận body tùy chọn cho request POST.
        [string]$IdempotencyKey = $null # Nhận key riêng cho mỗi lần POST để không trả lại đơn cũ.
    ) # Kết thúc khai báo tham số của hàm request.
    $httpMethod = [System.Net.Http.HttpMethod]::Get # Khởi tạo phương thức mặc định để xử lý các request GET.
    if ($Method -eq "POST") { $httpMethod = [System.Net.Http.HttpMethod]::Post } # Chọn POST khi endpoint tạo đơn cần body.
    if ($Method -eq "DELETE") { $httpMethod = [System.Net.Http.HttpMethod]::Delete } # Chọn DELETE khi endpoint hủy đơn.
    $request = [System.Net.Http.HttpRequestMessage]::new($httpMethod, [Uri]::new("$script:BaseUrl$Path")) # Tạo request với URL tuyệt đối.
    if ($null -ne $IdempotencyKey) { [void]$request.Headers.TryAddWithoutValidation("X-Idempotency-Key", $IdempotencyKey) } # Gửi key riêng để đo thao tác tạo mới, không đo kết quả idempotency cũ.
    if ($null -ne $Body) { $json = ConvertTo-Json -InputObject $Body -Depth 8 -Compress; $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json") } # Tuần tự hóa body JSON theo contract của Order API.
    $watch = [System.Diagnostics.Stopwatch]::StartNew() # Bắt đầu đo ngay trước khi gửi request để tính thời gian phản hồi.
    $response = $null # Khởi tạo response để khối finally có thể giải phóng an toàn khi gửi lỗi.
    try { # Bắt lỗi kết nối của riêng request và ghi nhận thay vì làm sai thống kê.
        $response = $script:HttpClient.SendAsync($request).GetAwaiter().GetResult() # Gửi request đồng bộ để không có tải song song.
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() # Đọc body trước khi dừng đồng hồ để tính đủ thời gian phản hồi.
        $watch.Stop() # Dừng đồng hồ sau khi đã nhận xong status và body.
        return [pscustomobject]@{ Status = [int]$response.StatusCode; ElapsedMs = [double]$watch.Elapsed.TotalMilliseconds; Body = $responseBody; Error = $null } # Trả status thật và latency thô để hàm thống kê lọc status mong đợi.
    } catch { # Giữ lỗi vận chuyển riêng biệt để không biến nó thành response thành công giả.
        $watch.Stop() # Dừng đồng hồ cả khi request gặp lỗi kết nối hoặc timeout.
        return [pscustomobject]@{ Status = "TRANSPORT_ERROR"; ElapsedMs = [double]$watch.Elapsed.TotalMilliseconds; Body = ""; Error = $_.Exception.Message } # Ghi rõ lỗi vận chuyển trong cột errors.
    } finally { # Giải phóng tài nguyên HTTP sau mỗi lần gọi để tránh giữ socket không cần thiết.
        if ($null -ne $response) { $response.Dispose() } # Đóng response sau khi đã đọc body.
        $request.Dispose() # Đóng request và content sau mỗi lần gọi.
    } # Kết thúc khối giải phóng request.
} # Kết thúc hàm gửi HTTP request.

function New-OrderPayload { # Tạo body theo DTO thật của POST /api/v1/orders.
    param( # Khai báo customerId cho payload.
        [Guid]$CustomerId # Nhận mã khách hàng cần gắn vào đơn.
    ) # Kết thúc khai báo tham số của hàm payload.
    return @{ customerId = $CustomerId; street = "12 Test Street"; city = "Hanoi"; country = "Vietnam"; postalCode = "10000"; items = @(@{ productId = $expectedProductId; quantity = 1; unitPriceAmount = 250000; unitPriceCurrency = "VND" }) } # Tạo đúng một item trị giá 250000 VND như dữ liệu kiểm thử Day 48/50.
} # Kết thúc hàm tạo body đơn hàng.

function New-SetupOrder { # Tạo đơn ngoài khoảng đo để làm dữ liệu đầu vào hợp lệ cho GET hoặc DELETE.
    param( # Khai báo dữ liệu setup cần dùng.
        [Guid]$CustomerId # Nhận customerId muốn gán cho đơn setup.
    ) # Kết thúc khai báo tham số của hàm setup.
    $key = [Guid]::NewGuid().ToString("N") # Tạo idempotency key riêng để mỗi đơn setup thực sự được tạo mới.
    $request = Invoke-ApiRequest -Method "POST" -Path "/api/v1/orders" -Body (New-OrderPayload -CustomerId $CustomerId) -IdempotencyKey $key # Gửi POST setup trước khi bắt đầu đo endpoint phụ thuộc.
    if ($request.Status -ne 201) { throw "Tạo dữ liệu setup thất bại: POST trả status $($request.Status); body=$($request.Body); error=$($request.Error)" } # Báo rõ status nếu không thể chuẩn bị dữ liệu đo.
    $json = $request.Body | ConvertFrom-Json # Đọc orderId mà API trả về để các request sau dùng đúng tài nguyên.
    if ($null -eq $json.orderId) { throw "POST setup trả 201 nhưng body không có orderId." } # Không tiếp tục đo nếu response không cung cấp ID hợp lệ.
    return [Guid]$json.orderId # Trả ID đơn setup để GET hoặc DELETE sử dụng.
} # Kết thúc hàm tạo đơn setup.

function Measure-Endpoint { # Đo tuần tự số request cố định và tính thống kê từ status mong đợi.
    param( # Khai báo thông tin endpoint và cách tạo từng request.
        [string]$Name, # Nhận tên endpoint để đưa vào bảng kết quả.
        [int]$ExpectedStatus, # Nhận status duy nhất được tính vào thống kê latency.
        [scriptblock]$RequestFactory # Nhận hàm tạo request cho mỗi chỉ số lặp.
    ) # Kết thúc khai báo tham số của hàm đo.
    $samples = [System.Collections.Generic.List[object]]::new() # Lưu kết quả từng request để có thể đếm status lỗi.
    for ($index = 1; $index -le $requestCount; $index++) { $samples.Add((& $RequestFactory $index)) } # Gửi đúng 100 request tuần tự, không tạo tác vụ song song.
    $successful = @($samples | Where-Object { $_.Status -eq $ExpectedStatus }) # Chỉ lấy latency của status mong đợi theo yêu cầu baseline.
    $latencies = @($successful | ForEach-Object { [double]$_.ElapsedMs } | Sort-Object) # Sắp xếp latency hợp lệ để tính percentile gần nhất.
    $errorSamples = @($samples | Where-Object { $_.Status -ne $ExpectedStatus }) # Giữ mọi status khác và lỗi vận chuyển để không che giấu lỗi.
    $errorGroups = @($errorSamples | Group-Object -Property Status | ForEach-Object { "$($_.Name) x $($_.Count)" }) # Gom status lỗi theo mã và số lần xuất hiện.
    $average = "N/A" # Để trống thống kê latency nếu không có request nào trả status mong đợi.
    $p50 = "N/A" # Để trống p50 nếu không có mẫu hợp lệ.
    $p99 = "N/A" # Để trống p99 nếu không có mẫu hợp lệ.
    $maximum = "N/A" # Để trống max nếu không có mẫu hợp lệ.
    if ($latencies.Count -gt 0) { $average = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", (($latencies | Measure-Object -Average).Average)) } # Tính avg chỉ từ request trả status đúng và định dạng hai chữ số thập phân.
    if ($latencies.Count -gt 0) { $p50Index = [Math]::Max(1, [Math]::Ceiling(0.50 * $latencies.Count)) - 1; $p50 = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $latencies[$p50Index]) } # Tính p50 theo nearest-rank trên các latency hợp lệ.
    if ($latencies.Count -gt 0) { $p99Index = [Math]::Max(1, [Math]::Ceiling(0.99 * $latencies.Count)) - 1; $p99 = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $latencies[$p99Index]) } # Tính p99 theo nearest-rank trên các latency hợp lệ.
    if ($latencies.Count -gt 0) { $maximum = [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $latencies[-1]) } # Ghi latency lớn nhất trong các response hợp lệ.
    return [pscustomobject]@{ Endpoint = $Name; Requests = $samples.Count; Success = $successful.Count; AvgMs = $average; P50Ms = $p50; P99Ms = $p99; MaxMs = $maximum; Errors = $errorSamples.Count; ErrorStatuses = [string]::Join(", ", $errorGroups); Samples = $samples } # Trả thống kê cùng mẫu thô để lưu kết quả kiểm tra.
} # Kết thúc hàm đo endpoint.

try { # Thực hiện kiểm tra sẵn sàng, setup và năm phép đo trong một lần chạy.
    $health = Invoke-ApiRequest -Method "GET" -Path "/health" # Kiểm tra API trước khi đo để không tạo baseline từ một service đã chết.
    if ($health.Status -ne 200) { throw "Order API chưa sẵn sàng: GET /health trả status $($health.Status); body=$($health.Body); error=$($health.Error)" } # Dừng với lỗi rõ ràng nếu health check không trả 200.
    Write-Output "Health check: GET /health = 200; bắt đầu đo tuần tự $requestCount request mỗi endpoint." # In xác nhận để người chạy biết API sống và phép đo không song song.
    $listCustomerId = [Guid]::NewGuid() # Tạo một customerId dùng chung để chuẩn bị tập dữ liệu list.
    for ($seedIndex = 1; $seedIndex -le 5; $seedIndex++) { [void](New-SetupOrder -CustomerId $listCustomerId) } # Tạo trước năm đơn cùng customerId để endpoint list có dữ liệu thực.
    $postMeasurement = Measure-Endpoint -Name "POST /api/v1/orders" -ExpectedStatus 201 -RequestFactory { param($index) $customerId = [Guid]::NewGuid(); $key = [Guid]::NewGuid().ToString("N"); $response = Invoke-ApiRequest -Method "POST" -Path "/api/v1/orders" -Body (New-OrderPayload -CustomerId $customerId) -IdempotencyKey $key; $orderId = $null; if ($response.Status -eq 201) { try { $parsed = $response.Body | ConvertFrom-Json; if ($null -ne $parsed.orderId) { $orderId = [Guid]$parsed.orderId } else { $response.Status = "INVALID_RESPONSE" } } catch { $response.Status = "INVALID_RESPONSE"; $response.Error = $_.Exception.Message } }; [pscustomobject]@{ Status = $response.Status; ElapsedMs = $response.ElapsedMs; OrderId = $orderId; Error = $response.Error } } # Dùng customerId và idempotency key mới ở mỗi lần POST để tránh nhận lại đơn cũ do idempotency.
    $referenceOrderIds = @($postMeasurement.Samples | Where-Object { $null -ne $_.OrderId } | ForEach-Object { $_.OrderId }) # Dùng các orderId thật vừa tạo bởi lượt POST làm dữ liệu đầu vào cho GET.
    if ($referenceOrderIds.Count -eq 0) { $referenceOrderIds = @(New-SetupOrder -CustomerId ([Guid]::NewGuid())) } # Tạo ID dự phòng để vẫn ghi nhận đủ request GET nếu toàn bộ POST đo lỗi.
    $detailMeasurement = Measure-Endpoint -Name "GET /api/v1/orders/{id}" -ExpectedStatus 200 -RequestFactory { param($index) $orderId = $referenceOrderIds[($index - 1) % $referenceOrderIds.Count]; $response = Invoke-ApiRequest -Method "GET" -Path "/api/v1/orders/$orderId"; [pscustomobject]@{ Status = $response.Status; ElapsedMs = $response.ElapsedMs; Error = $response.Error } } # Dùng tuần hoàn các orderId vừa tạo để đo đủ 100 lần GET.
    $listMeasurement = Measure-Endpoint -Name "GET /api/v1/orders?customerId=...&pageSize=20" -ExpectedStatus 200 -RequestFactory { param($index) $response = Invoke-ApiRequest -Method "GET" -Path "/api/v1/orders?customerId=$listCustomerId&pageSize=20"; [pscustomobject]@{ Status = $response.Status; ElapsedMs = $response.ElapsedMs; Error = $response.Error } } # Dùng customerId đã có năm đơn setup để đo cùng một truy vấn list 100 lần.
    $deleteMeasurement = Measure-Endpoint -Name "DELETE /api/v1/orders/{id}" -ExpectedStatus 204 -RequestFactory { param($index) $orderId = New-SetupOrder -CustomerId ([Guid]::NewGuid()); $response = Invoke-ApiRequest -Method "DELETE" -Path "/api/v1/orders/$orderId"; [pscustomobject]@{ Status = $response.Status; ElapsedMs = $response.ElapsedMs; Error = $response.Error } } # Tạo đơn mới trước mỗi DELETE và chỉ tính thời gian DELETE vì xóa lặp cùng ID sẽ trả lỗi.
    $v2Measurement = Measure-Endpoint -Name "GET /api/v2/orders/{id}" -ExpectedStatus 200 -RequestFactory { param($index) $orderId = $referenceOrderIds[($index - 1) % $referenceOrderIds.Count]; $response = Invoke-ApiRequest -Method "GET" -Path "/api/v2/orders/$orderId"; [pscustomobject]@{ Status = $response.Status; ElapsedMs = $response.ElapsedMs; Error = $response.Error } } # Tái sử dụng orderId đã tạo để đo contract GET v2 trên cùng dữ liệu có thật.
    $measurements = @($postMeasurement, $detailMeasurement, $listMeasurement, $deleteMeasurement, $v2Measurement) # Giữ đúng thứ tự năm endpoint trong checklist Day 51.
    $rows = @($measurements | Select-Object Endpoint, Requests, Success, AvgMs, P50Ms, P99Ms, MaxMs, Errors, ErrorStatuses) # Chuẩn bị bảng hiển thị gồm đủ latency và lỗi.
    $table = $rows | Format-Table -AutoSize | Out-String -Width 240 # Tạo bảng rộng để không cắt tên endpoint hoặc status lỗi.
    $dockerVersion = (& docker version --format '{{.Client.Version}} / {{.Server.Version}}' 2>$null | Select-Object -First 1) # Ghi phiên bản Docker thực tế của máy đo nếu CLI truy vấn được.
    if ([string]::IsNullOrWhiteSpace($dockerVersion)) { $dockerVersion = "không lấy được từ docker CLI" } # Nêu rõ thiếu metadata thay vì tự điền một phiên bản giả.
    $report = @("ShopFlow Day 51 - Kết quả đo Order API", "Thời điểm ghi báo cáo: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))", "Máy: $env:COMPUTERNAME", "Hệ điều hành: $([Environment]::OSVersion.VersionString)", "PowerShell: $($PSVersionTable.PSVersion)", "Docker: $dockerVersion", "BaseUrl: $script:BaseUrl", "Cách đo: 100 request tuần tự mỗi endpoint; latency chỉ tính status mong đợi; p50/p99 dùng nearest-rank.", "", $table.TrimEnd()) -join [Environment]::NewLine # Ghép kết quả và metadata để lần đo có thể kiểm chứng lại.
    $outputPath = Join-Path $PSScriptRoot "baseline-day51.txt" # Lưu bản output cạnh script theo yêu cầu để tài liệu baseline đối chiếu trực tiếp.
    Set-Content -LiteralPath $outputPath -Value $report -Encoding UTF8 # Ghi kết quả thật do script vừa đo, không nhập số bằng tay.
    Write-Output $report # In cùng nội dung ra console để người chạy có thể lưu và đối chiếu.
    if (@($measurements | Where-Object { $_.Errors -gt 0 }).Count -gt 0) { Write-Warning "Có request lỗi; xem cột Errors và ErrorStatuses; request lỗi không được tính vào percentile." } # Cảnh báo khi có response sai status mà vẫn giữ đủ bằng chứng trong báo cáo.
} finally { # Giải phóng HttpClient kể cả khi health check hoặc dữ liệu setup gặp lỗi.
    $script:HttpClient.Dispose() # Đóng client để giải phóng connection pool khi script kết thúc.
} # Kết thúc khối chạy và giải phóng tài nguyên.


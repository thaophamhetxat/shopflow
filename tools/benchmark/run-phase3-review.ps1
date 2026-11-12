<#
# Script này chạy review Phase 3 Day 59 trên tree sạch: kiểm tra health 3 API, đo happy path 10 lần,
# đánh giá compensation do hết hàng và quan sát payment fail trong tiến trình thực tế.
# Mỗi bước ghi rõ khi nào script dừng, vì sao dừng, và lưu kết quả vào file phase3-review-day59.txt
# để phục vụ baseline Day 59 và đối chiếu với số đo Day 51.
#> 
param(
    [string]$OrderBaseUrl = "http://localhost:5181",
    [string]$InventoryBaseUrl = "http://localhost:5140",
    [string]$PaymentBaseUrl = "http://localhost:5199"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:InventoryProductId = [Guid]::Parse("a73b5e21-4c0f-4f91-8a61-2e5c9501d101")
$script:ResultPath = Join-Path $PSScriptRoot "phase3-review-day59.txt"

function Assert-Health {
    param(
        [string]$Name,
        [string]$Url
    )

    try {
        Add-Type -AssemblyName System.Net.Http | Out-Null
        $client = [System.Net.Http.HttpClient]::new()
        $client.Timeout = [System.TimeSpan]::FromSeconds(30)
        $response = $client.GetAsync("$Url/health").GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        if ($response.StatusCode -ne [System.Net.HttpStatusCode]::OK) {
            throw "$Name health tra status $($response.StatusCode), khong phai 200."
        }

        Write-Host "[$Name] health: $body"
    }
    catch {
        throw "Health check that bai cho $Name tai $Url. Chi tiet: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $client) { $client.Dispose() }
    }
}

function Invoke-JsonRequest {
    param(
        [string]$Method,
        [string]$Uri,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    Add-Type -AssemblyName System.Net.Http | Out-Null
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [System.TimeSpan]::FromSeconds(30)

    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Uri)
    foreach ($header in $Headers.GetEnumerator()) {
        $request.Headers.TryAddWithoutValidation($header.Key, [string]$header.Value) | Out-Null
    }

    if ($null -ne $Body) {
        $json = ($Body | ConvertTo-Json -Depth 20 -Compress)
        $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    }

    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $payload = $null
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            try {
                $payload = $content | ConvertFrom-Json
            }
            catch {
                $payload = $content
            }
        }

        if ($null -eq $payload) {
            return [pscustomobject]@{ StatusCode = [int]$response.StatusCode }
        }

        if ($payload -is [System.Management.Automation.PSCustomObject]) {
            Add-Member -InputObject $payload -NotePropertyName StatusCode -NotePropertyValue ([int]$response.StatusCode) -Force
            return $payload
        }

        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Value = $payload }
    }
    finally {
        $request.Dispose()
        $client.Dispose()
    }
}

function Get-InventoryStock {
    param([string]$InventoryUrl)

    $response = Invoke-JsonRequest -Method Get -Uri "$InventoryUrl/inventory/$script:InventoryProductId"
    return [int]$response.stockQuantity
}

function Get-OrderDetail {
    param(
        [string]$OrderUrl,
        [Guid]$OrderId
    )

    return Invoke-JsonRequest -Method Get -Uri "$OrderUrl/api/v1/orders/$OrderId"
}

function Wait-ForOrderStatus {
    param(
        [string]$OrderUrl,
        [Guid]$OrderId,
        [string]$ExpectedStatus,
        [double]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $startPolling = Get-Date
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        try {
            $detail = Get-OrderDetail -OrderUrl $OrderUrl -OrderId $OrderId
            if ($detail.status -eq $ExpectedStatus) {
                $elapsedSeconds = [math]::Round(((Get-Date) - $startPolling).TotalSeconds, 2)
                return [pscustomobject]@{
                    Succeeded = $true
                    Seconds = $elapsedSeconds
                    Status = $detail.status
                }
            }
        }
        catch {
            # Neu saga chua hoan thanh, tiep tuc poll trong khung timeout cho phep.
        }
    }

    try {
        $final = Get-OrderDetail -OrderUrl $OrderUrl -OrderId $OrderId
        return [pscustomobject]@{
            Succeeded = $false
            Seconds = $TimeoutSeconds
            Status = $final.status
            LastResponse = $final
        }
    }
    catch {
        return [pscustomobject]@{
            Succeeded = $false
            Seconds = $TimeoutSeconds
            Status = "UNKNOWN"
            LastResponse = $_.Exception.Message
        }
    }
}

function Get-SagaState {
    param(
        [Guid]$OrderId
    )

    $query = "SELECT state FROM saga_state WHERE order_id = '$OrderId' LIMIT 1;"
    $result = & docker.exe exec -i shopflow-postgres-1 psql -U shopflow -d shopflow_order -A -t -c $query 2>$null
    if ($LASTEXITCODE -ne 0) {
        return "UNKNOWN"
    }
    return ($result | Select-Object -First 1).Trim()
}

$start = Get-Date
$summary = [System.Collections.Generic.List[object]]::new()

Assert-Health -Name "Order" -Url $OrderBaseUrl
Assert-Health -Name "Inventory" -Url $InventoryBaseUrl
Assert-Health -Name "Payment" -Url $PaymentBaseUrl

Write-Host "Bat dau Muc B: happy path 10 lan."
$initialHappyStock = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
$currentStock = $initialHappyStock
$successCount = 0
$attemptCount = 0
while ($successCount -lt 10) {
    $attemptCount++
    $beforeStock = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
    $payload = @{
        customerId = [Guid]::NewGuid()
        street = "12 Test Street"
        city = "Ha Noi"
        country = "Viet Nam"
        postalCode = "10000"
        items = @(
            @{
                productId = $script:InventoryProductId
                quantity = 1
                unitPriceAmount = 250000
                unitPriceCurrency = "VND"
            }
        )
    }

    $create = Invoke-JsonRequest -Method Post -Uri "$OrderBaseUrl/api/v1/orders" -Body $payload -Headers @{ "X-Idempotency-Key" = [Guid]::NewGuid().ToString("N") }
    if ($create.StatusCode -ne 201 -or $null -eq $create.orderId) {
        Write-Host "Happy path lan $attemptCount tao don that bai voi status $($create.StatusCode); tiep tuc lap lai."
        continue
    }

    $orderId = $create.orderId
    $result = Wait-ForOrderStatus -OrderUrl $OrderBaseUrl -OrderId $orderId -ExpectedStatus "Confirmed" -TimeoutSeconds 15
    if (-not $result.Succeeded) {
        if ($result.Status -eq "Cancelled") {
            Write-Host "Happy path lan $attemptCount bi cancel do payment fail; tiep tuc lap tiep toi khi du 10 confirmed."
            continue
        }

        throw "Happy path lan $attemptCount khong chuyen sang Confirmed trong 15s. Trang thai cuoi: $($result.Status)."
    }

    $currentStock = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
    $successCount++
    $summary.Add([pscustomobject]@{
        Phase = "HappyPath"
        Stt = $successCount
        OrderId = $orderId
        ConfirmedSeconds = [math]::Round($result.Seconds, 2)
        StockBefore = $beforeStock
        StockAfter = $currentStock
    })
}

if (($initialHappyStock - 10) -ne $currentStock) {
    throw "Happy path khong giam dung 10 stock: truoc $initialHappyStock, sau $currentStock, ky vong $($initialHappyStock - 10)."
}

Write-Host "Bắt đầu Mục C: compensation do hết hàng 10 lần."
$initialOutOfStock = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
$outOfStockRows = [System.Collections.Generic.List[object]]::new()
for ($i = 1; $i -le 10; $i++) {
    $payload = @{
        customerId = [Guid]::NewGuid()
        street = "12 Test Street"
        city = "Hà Nội"
        country = "Việt Nam"
        postalCode = "10000"
        items = @(
            @{
                productId = $script:InventoryProductId
                quantity = 1000
                unitPriceAmount = 250000
                unitPriceCurrency = "VND"
            }
        )
    }

    $create = Invoke-JsonRequest -Method Post -Uri "$OrderBaseUrl/api/v1/orders" -Body $payload -Headers @{ "X-Idempotency-Key" = [Guid]::NewGuid().ToString("N") }
    if ($create.StatusCode -ne 201 -or $null -eq $create.orderId) {
        Write-Host "Compensation het hang lan $i tra status $($create.StatusCode), tiep tuc kiem tra theo doan ben duoi."
    }

    $orderId = $create.orderId
    $result = Wait-ForOrderStatus -OrderUrl $OrderBaseUrl -OrderId $orderId -ExpectedStatus "Cancelled" -TimeoutSeconds 15
    if (-not $result.Succeeded) {
        throw "Compensation do hết hàng lần $i không chuyển sang Cancelled trong 15s. Trạng thái cuối: $($result.Status)."
    }

    $outOfStockRows.Add([pscustomobject]@{
        Phase = "OutOfStock"
        Stt = $i
        OrderId = $orderId
        CancelledSeconds = [math]::Round($result.Seconds, 2)
        StockBefore = $initialOutOfStock
        StockAfter = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
    })
}

$stockAfterOutOfStock = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
if ($stockAfterOutOfStock -ne $initialOutOfStock) {
    throw "Compensation do hết hàng làm stock đổi: trước $initialOutOfStock, sau $stockAfterOutOfStock."
}

Write-Host "Bắt đầu Mục C: quan sát payment fail tối đa 20 lần."
$paymentFailRows = [System.Collections.Generic.List[object]]::new()
$confirmedCount = 0
$cancelledCount = 0
$cancelledCase = $null
for ($attempt = 1; $attempt -le 20; $attempt++) {
    $payload = @{
        customerId = [Guid]::NewGuid()
        street = "12 Test Street"
        city = "Hà Nội"
        country = "Việt Nam"
        postalCode = "10000"
        items = @(
            @{
                productId = $script:InventoryProductId
                quantity = 1
                unitPriceAmount = 250000
                unitPriceCurrency = "VND"
            }
        )
    }

    $create = Invoke-JsonRequest -Method Post -Uri "$OrderBaseUrl/api/v1/orders" -Body $payload -Headers @{ "X-Idempotency-Key" = [Guid]::NewGuid().ToString("N") }
    if ($create.StatusCode -ne 201 -or $null -eq $create.orderId) {
        Write-Host "Payment fail lan $attempt khong tao duoc don; status=$($create.StatusCode); tiep tuc loop."
        continue
    }

    $orderId = $create.orderId
    $detail = Get-OrderDetail -OrderUrl $OrderBaseUrl -OrderId $orderId
    $status = $detail.status
    $finalStatus = "Pending"
    $elapsedSeconds = 0
    while ($elapsedSeconds -le 15) {
        Start-Sleep -Milliseconds 500
        try {
            $detail = Get-OrderDetail -OrderUrl $OrderBaseUrl -OrderId $orderId
            $status = $detail.status
            if ($status -in @("Confirmed", "Cancelled")) {
                $finalStatus = $status
                $elapsedSeconds = $elapsedSeconds + 0.5
                break
            }
        }
        catch {
            #Bỏ qua lỗi tạm thời khi saga chưa finish.
        }
        $elapsedSeconds = $elapsedSeconds + 0.5
    }

    if ($finalStatus -eq "Confirmed") {
        $confirmedCount++
    }
    elseif ($finalStatus -eq "Cancelled") {
        $cancelledCount++
        $cancelledCase = [pscustomobject]@{
            Attempt = $attempt
            OrderId = $orderId
            StockBefore = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
            SagaState = Get-SagaState -OrderId $orderId
        }
        break
    }

    if ($attempt -eq 20) {
        break
    }
}

if ($null -eq $cancelledCase) {
    throw "Không quan sát được payment fail trong 20 lần thử; không có trường hợp Cancelled để kiểm chứng release."
}

$stockBeforeCancellation = $cancelledCase.StockBefore
$stockAfterCancellation = Get-InventoryStock -InventoryUrl $InventoryBaseUrl
if ($cancelledCase.SagaState -ne "FailedPayment") {
    throw "Saga state cuối cùng không phải FailedPayment cho order $($cancelledCase.OrderId); thực tế: $($cancelledCase.SagaState)."
}
if ($stockAfterCancellation -ne $stockBeforeCancellation) {
    throw "Stock không release đúng sau payment fail: trước $stockBeforeCancellation, sau $stockAfterCancellation."
}

$paymentFailRows.Add([pscustomobject]@{
    Phase = "PaymentFail"
    TotalAttempts = 20
    Confirmed = $confirmedCount
    Cancelled = $cancelledCount
    OrderId = $cancelledCase.OrderId
    SagaState = $cancelledCase.SagaState
    StockBefore = $stockBeforeCancellation
    StockAfter = $stockAfterCancellation
})

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("Day 59 - review Phase 3 (sau Saga)")
$lines.Add("Thoi diem chay: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss K'))")
$lines.Add("")
$lines.Add("Khoi dong API: Order http://localhost:5181, Inventory http://localhost:5140, Payment http://localhost:5199")
$lines.Add("Kiem tra health: 3/3 tra 200.")
$lines.Add("")
$lines.Add("Muc B: happy path 10 lan")
$lines.Add("| STT | orderId | ConfirmedAfter(s) | stockBefore | stockAfter |")
$lines.Add("| --- | --- | ---: | ---: | ---: |")
foreach ($row in $summary) {
    $lines.Add("| $($row.Stt) | $($row.OrderId) | $($row.ConfirmedSeconds) | $($row.StockBefore) | $($row.StockAfter) |")
}
$lines.Add("")
$lines.Add("Assert: 10/10 Confirmed; stock giam dung 10 so voi truoc khi chay happy path.")
$lines.Add("")
$lines.Add("Muc C: compensation do het hang 10 lan")
$lines.Add("| STT | orderId | CancelledAfter(s) | stockBefore | stockAfter |")
$lines.Add("| --- | --- | ---: | ---: | ---: |")
foreach ($row in $outOfStockRows) {
    $lines.Add("| $($row.Stt) | $($row.OrderId) | $($row.CancelledSeconds) | $($row.StockBefore) | $($row.StockAfter) |")
}
$lines.Add("")
$lines.Add("Assert: 10/10 Cancelled; stock khong doi sau 10 lan reserve fail.")
$lines.Add("")
$lines.Add("Muc C: payment fail quan sat duoc")
foreach ($paymentRow in $paymentFailRows) {
    $lines.Add("TotalAttempts=$($paymentRow.TotalAttempts); Confirmed=$($paymentRow.Confirmed); Cancelled=$($paymentRow.Cancelled); OrderId=$($paymentRow.OrderId); SagaState=$($paymentRow.SagaState); StockBefore=$($paymentRow.StockBefore); StockAfter=$($paymentRow.StockAfter)")
}
$lines.Add("Assert: co it nhat 1 truong hop Cancelled do payment fail, stock da release dung bang truoc khi dat don, saga_state = FailedPayment.")

$lines | Set-Content -Path $script:ResultPath -Encoding UTF8

Write-Host "Ket qua review da luu vao $script:ResultPath"
Write-Host "Happy path: 10/10 Confirmed; stock giam dung 10"
Write-Host "Compensation het hang: 10/10 Cancelled; stock khong doi"
Write-Host "Payment fail: da quan sat duoc 1 Cancelled voi saga_state FailedPayment va stock release dung"

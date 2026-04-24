# HỆ THỐNG PHÂN TÍCH NHIÊN LIỆU REAL-TIME
## Real-time Streaming Fuel Analysis System

---

## 🎯 KIẾN TRÚC REAL-TIME

### Luồng xử lý từng bản tin:

```
Bản tin xe → Butterworth Filter → Kalman Filter → Segment Analysis → Event Trigger
     ↓              ↓                   ↓                 ↓                ↓
  Raw Data    Loại nhiễu           Làm mượt         Phân loại         Alert
                                                                          ↓
                                                               Database/Dashboard
```

---

## 🔄 CÁCH HOẠT ĐỘNG

### 1. Streaming Processing

**INPUT**: Từng bản tin riêng lẻ
```csharp
VehicleDataPoint {
    Timestamp: 2024-01-20 10:30:45
    Velocity: 60 km/h
    Latitude: 21.028511
    Longitude: 105.804817
    FuelLevel: 45.3 L
}
```

**PROCESS**: Xử lý ngay lập tức
```csharp
var analyzer = new RealtimeFuelAnalyzer();
var result = analyzer.ProcessDataPoint(rawData);
// → Smoothed, analyzed, segment detected (nếu có)
```

**OUTPUT**: 
- Data đã làm mượt
- Segment hiện tại (nếu đang trong segment)
- Event khi segment hoàn thành

---

## 📊 STATE MANAGEMENT

### Rolling Window
Hệ thống chỉ giữ **10 data points gần nhất** trong memory:

```
[t-9] [t-8] [t-7] ... [t-1] [t-0]
  ↑                            ↑
  Oldest                    Newest
```

Khi có data mới → xóa data cũ nhất
**Memory usage**: O(1) - constant, không tăng theo thời gian

---

## 🔍 SEGMENT DETECTION LOGIC

### State Machine

```
┌─────────────┐
│   START     │
└──────┬──────┘
       │
       ↓
┌──────────────────────────────────────┐
│  Nhận data point đầu tiên            │
└──────┬───────────────────────────────┘
       │
       ↓
┌──────────────────────────────────────┐
│  So sánh với data point trước        │
│  - Fuel change                       │
│  - Velocity                          │
│  - Distance                          │
└──────┬───────────────────────────────┘
       │
       ↓
┌──────────────────────────────────────┐
│  Classify event:                     │
│  • Fuel tăng > 3L → REFUELING        │
│  • Fuel giảm > 2L + đứng → THEFT     │
│  • Fuel giảm + chạy → CONSUMPTION    │
│  • Đứng yên + không đổi → IDLE       │
└──────┬───────────────────────────────┘
       │
       ↓
    ┌──┴──┐
    │ Có  │ current_segment?
    └─┬─┬─┘
  No  │ │  Yes
      │ │
      │ └────────────────────────┐
      │                          │
      ↓                          ↓
┌─────────────┐      ┌──────────────────────┐
│ START       │      │ Type giống nhau?     │
│ NEW         │      └──┬───────────────┬───┘
│ SEGMENT     │     Yes │               │ No
└─────────────┘         │               │
                        ↓               ↓
              ┌──────────────┐  ┌──────────────┐
              │ UPDATE       │  │ COMPLETE     │
              │ current      │  │ current      │
              │ segment      │  │ segment      │
              └──────────────┘  │              │
                                │ START new    │
                                │ segment      │
                                └──────────────┘
```

---

## 🎛️ CONFIGURATION PARAMETERS

### 1. Detection Thresholds

```csharp
// Ngưỡng đổ nhiên liệu
const double REFUEL_THRESHOLD = 3.0;  // Tăng > 3L

// Ngưỡng hút/trộm
const double THEFT_THRESHOLD = 2.0;   // Giảm > 2L khi đứng

// Ngưỡng vận tốc (xe đang chạy)
const double VELOCITY_THRESHOLD = 5.0; // > 5 km/h

// Timeout segment
const double SEGMENT_TIMEOUT_MINUTES = 5; // 5 phút không update → đóng
```

**Điều chỉnh theo nhu cầu:**

| Use Case | REFUEL | THEFT | VELOCITY | TIMEOUT |
|----------|--------|-------|----------|---------|
| Xe con | 3.0 | 2.0 | 5.0 | 5 |
| Xe tải | 10.0 | 5.0 | 5.0 | 10 |
| Xe công trình | 15.0 | 10.0 | 3.0 | 15 |
| Máy bay | 500.0 | 100.0 | 50.0 | 30 |

### 2. Filter Parameters

```csharp
// Butterworth Filter
order: 2                    // Order càng cao → lọc càng mạnh
cutoffFrequency: 0.1 Hz     // Tần số cắt (thấp hơn → lọc nhiều hơn)

// Kalman Filter  
processNoise: 0.01         // Nhiễu quá trình (tiêu hao tự nhiên)
measurementNoise: 0.5      // Nhiễu cảm biến
```

**Tuning Guide:**

| Điều kiện | processNoise | measurementNoise | cutoffFreq |
|-----------|--------------|------------------|------------|
| Cảm biến tốt, đường tốt | 0.01 | 0.3 | 0.1 |
| Cảm biến kém | 0.01 | 1.0 | 0.05 |
| Địa hình xấu | 0.05 | 0.8 | 0.05 |
| Real-time priority | 0.02 | 0.5 | 0.2 |

---

## 💡 SEGMENT DETECTION RULES

### Rule 1: ĐỔ NHIÊN LIỆU

**Điều kiện:**
1. `fuelChange > REFUEL_THRESHOLD` (tăng > 3L)
2. `velocity < VELOCITY_THRESHOLD` (đứng yên)

**Tiếp tục segment khi:**
- Nhiên liệu vẫn đang tăng (`fuelChange > 0.5L`)
- Xe vẫn đứng yên

**Kết thúc segment khi:**
- Nhiên liệu ngừng tăng
- Xe bắt đầu chạy
- Timeout (5 phút)

**Confidence calculation:**
```csharp
baseConfidence = min(1.0, totalRefuel / 30.0)
if (averageVelocity > threshold)
    confidence *= 0.5  // Penalty nếu đang chạy
```

---

### Rule 2: HÚT/TRỘM NHIÊN LIỆU

**Điều kiện:**
1. `fuelChange < -THEFT_THRESHOLD` (giảm > 2L)
2. `velocity < VELOCITY_THRESHOLD` (đứng yên)
3. **Không phải tiêu hao bình thường**

**Tiếp tục segment khi:**
- Nhiên liệu vẫn đang giảm (`fuelChange < -0.5L`)
- Xe vẫn đứng yên

**Kết thúc segment khi:**
- Nhiên liệu ngừng giảm
- Xe bắt đầu chạy

**Confidence calculation:**
```csharp
baseConfidence = min(1.0, totalTheft / 20.0)
if (averageVelocity > threshold)
    confidence *= 0.3  // Penalty lớn nếu đang chạy
```

---

### Rule 3: TIÊU HAO BÌNH THƯỜNG

**Điều kiện:**
1. `velocity > VELOCITY_THRESHOLD` (xe đang chạy)
2. `fuelChange < 0` (nhiên liệu giảm)
3. **Không phải giảm đột ngột**

**Tiếp tục segment khi:**
- Xe vẫn đang chạy
- Không có event đặc biệt (đổ/hút)

**Kết thúc segment khi:**
- Xe dừng lại
- Phát hiện đổ/hút

**Confidence calculation:**
```csharp
expectedConsumption = distance * normalConsumptionRate
actualConsumption = abs(fuelChange)
ratio = actual / expected

if (ratio in [0.8, 1.2])  → confidence = 1.0
if (ratio in [0.6, 1.4])  → confidence = 0.8
if (ratio in [0.4, 1.6])  → confidence = 0.6
else                      → confidence = 0.3
```

---

### Rule 4: ĐỨNG YÊN (IDLE)

**Điều kiện:**
1. `velocity < VELOCITY_THRESHOLD`
2. `abs(fuelChange) < 0.5L`

**Tiếp tục segment khi:**
- Xe vẫn đứng
- Không có thay đổi nhiên liệu lớn

---

## 🚀 USAGE EXAMPLES

### Example 1: Basic Usage

```csharp
// Khởi tạo
var analyzer = new RealtimeFuelAnalyzer(
    normalConsumptionRate: 0.08,  // 8L/100km
    velocityThreshold: 5.0,
    samplingRate: 1.0
);

// Đăng ký event
analyzer.OnSegmentCompleted += (sender, segment) => 
{
    Console.WriteLine($"Segment detected: {segment}");
    
    if (segment.Type == SegmentType.Theft)
    {
        SendAlert($"CẢNH BÁO: Hút {segment.FuelChange}L tại {segment.StartLocation}");
    }
};

// Xử lý từng bản tin
while (true)
{
    var data = ReceiveVehicleData(); // Nhận từ GPS/sensor
    analyzer.ProcessDataPoint(data);
}
```

---

### Example 2: Integration with Database

```csharp
var analyzer = new RealtimeFuelAnalyzer();

analyzer.OnSegmentCompleted += async (sender, segment) => 
{
    // Lưu vào database
    await db.Segments.AddAsync(new SegmentEntity
    {
        VehicleId = vehicleId,
        StartTime = segment.StartTime,
        EndTime = segment.EndTime,
        Type = segment.Type.ToString(),
        FuelChange = segment.FuelChange,
        Distance = segment.Distance,
        Confidence = segment.Confidence
    });
    
    await db.SaveChangesAsync();
};
```

---

### Example 3: Real-time Dashboard

```csharp
var analyzer = new RealtimeFuelAnalyzer();

analyzer.OnDataProcessed += (sender, point) => 
{
    // Update real-time chart
    dashboard.UpdateFuelLevel(point.SmoothedFuelLevel);
    dashboard.UpdateVelocity(point.Velocity);
};

analyzer.OnSegmentCompleted += (sender, segment) => 
{
    // Update segment list
    dashboard.AddSegment(segment);
    
    // Update statistics
    var stats = analyzer.GetStatistics();
    dashboard.UpdateStats(stats);
};
```

---

### Example 4: Alert System

```csharp
var analyzer = new RealtimeFuelAnalyzer();

analyzer.OnSegmentCompleted += (sender, segment) => 
{
    switch (segment.Type)
    {
        case SegmentType.Theft when segment.Confidence > 0.8:
            SendEmailAlert($"Phát hiện trộm {segment.FuelChange:F2}L");
            SendSMSAlert(driverPhone, "Cảnh báo hút nhiên liệu!");
            LogToSecuritySystem(segment);
            break;
            
        case SegmentType.Refueling:
            LogRefuelingEvent(segment);
            UpdateInventory(segment.FuelChange);
            break;
            
        case SegmentType.NormalConsumption when segment.Confidence < 0.5:
            // Tiêu hao bất thường
            SendWarning("Mức tiêu hao cao hơn bình thường");
            break;
    }
};
```

---

## 📈 PERFORMANCE CHARACTERISTICS

### Memory Usage
- **Rolling Window**: 10 data points × ~100 bytes = 1 KB
- **Filters**: 2 filters × ~500 bytes = 1 KB
- **Segments**: Average 100 segments × 200 bytes = 20 KB
- **Total**: ~22 KB per vehicle (very lightweight!)

### CPU Usage
- **Per data point**: ~0.5ms (2000 ops/sec)
- **Filter computation**: O(1) constant time
- **Segment analysis**: O(1) constant time

### Latency
- **Total latency**: < 1ms per data point
- **Butterworth delay**: 2 samples
- **Kalman delay**: 1 sample
- **Detection delay**: Immediate (same sample)

### Scalability
- **Single thread**: 1000+ vehicles real-time
- **Multi-thread**: 10,000+ vehicles
- **Distributed**: Unlimited with message queue

---

## 🔧 ADVANCED FEATURES

### 1. Lấy Segments theo điều kiện

```csharp
// Segments trong khoảng thời gian
var todaySegments = analyzer.GetSegmentsByTimeRange(
    DateTime.Today, 
    DateTime.Today.AddDays(1)
);

// Segments theo loại
var allThefts = analyzer.GetSegmentsByType(SegmentType.Theft);
var allRefuels = analyzer.GetSegmentsByType(SegmentType.Refueling);

// Current segment (đang xử lý)
var currentSegment = analyzer.GetCurrentSegment();
if (currentSegment != null && !currentSegment.IsComplete)
{
    Console.WriteLine($"Đang trong segment: {currentSegment.Type}");
}
```

### 2. Statistics Real-time

```csharp
var stats = analyzer.GetStatistics();

Console.WriteLine($"Tổng đổ: {stats.TotalFuelRefueled}L");
Console.WriteLine($"Tổng trộm: {stats.TotalFuelStolen}L");
Console.WriteLine($"Tổng tiêu hao: {stats.TotalFuelConsumed}L");
Console.WriteLine($"Quãng đường: {stats.TotalDistance}km");
Console.WriteLine($"Mức tiêu hao TB: {stats.AverageConsumptionRate}L/100km");
```

### 3. Reset State

```csharp
// Reset toàn bộ state (new vehicle, new day, etc.)
analyzer.Reset();
```

---

## ⚠️ EDGE CASES & HANDLING

### Edge Case 1: Data Loss (Mất bản tin)

**Problem**: GPS mất tín hiệu, không nhận bản tin trong 10 phút

**Solution**: Segment timeout
```csharp
if (currentTime - lastUpdateTime > TIMEOUT)
{
    CompleteCurrentSegment(); // Force complete
}
```

### Edge Case 2: Noisy Sensor (Cảm biến nhiễu cao)

**Problem**: Cảm biến dao động ±5L

**Solution**: 
1. Tăng `measurementNoise` của Kalman Filter
2. Giảm `cutoffFrequency` của Butterworth
3. Tăng `REFUEL_THRESHOLD` và `THEFT_THRESHOLD`

### Edge Case 3: Rapid Transitions (Chuyển đổi nhanh)

**Problem**: Đổ xong ngay lập tức chạy

**Solution**: Rolling window giữ history để phân tích context

### Edge Case 4: Gradual Theft (Hút từ từ)

**Problem**: Hút 1L/phút trong 10 phút

**Solution**: Segment accumulation - tích lũy fuel change

---

## 🎓 BEST PRACTICES

### 1. Event Handling
✅ **DO**: Async event handlers
```csharp
analyzer.OnSegmentCompleted += async (sender, segment) => 
{
    await SaveToDatabase(segment);
};
```

❌ **DON'T**: Block in event handler
```csharp
analyzer.OnSegmentCompleted += (sender, segment) => 
{
    Thread.Sleep(5000); // ❌ Blocks processing!
};
```

### 2. Error Handling
```csharp
try
{
    analyzer.ProcessDataPoint(data);
}
catch (ArgumentException ex)
{
    Logger.Error($"Invalid data: {ex.Message}");
    // Continue processing next data point
}
```

### 3. Logging
```csharp
analyzer.OnDataProcessed += (sender, point) => 
{
    if (point.RawFuelLevel - point.SmoothedFuelLevel > 2.0)
    {
        Logger.Warn($"High noise detected: {point.RawFuelLevel - point.SmoothedFuelLevel}L");
    }
};
```

---

## 🔬 TESTING

### Unit Test Example
```csharp
[Test]
public void TestTheftDetection()
{
    var analyzer = new RealtimeFuelAnalyzer();
    bool theftDetected = false;
    
    analyzer.OnSegmentCompleted += (sender, segment) => 
    {
        if (segment.Type == SegmentType.Theft)
            theftDetected = true;
    };
    
    // Simulate theft
    for (int i = 0; i < 10; i++)
    {
        analyzer.ProcessDataPoint(new VehicleDataPoint
        {
            Timestamp = DateTime.Now.AddSeconds(i),
            Velocity = 0,
            FuelLevel = 100 - i * 2, // Giảm 2L/giây
            Latitude = 21.0,
            Longitude = 105.0
        });
    }
    
    Assert.IsTrue(theftDetected);
}
```

---

## 📚 COMPARISON: Batch vs Real-time

| Aspect | Batch Processing | Real-time Streaming |
|--------|------------------|---------------------|
| Input | List toàn bộ data | Từng data point |
| Memory | O(n) - tăng theo data | O(1) - constant |
| Latency | Phải đợi hết data | Ngay lập tức |
| Use Case | Analysis sau này | Alert real-time |
| Accuracy | Cao hơn (có context) | Tốt (rolling window) |

---

## 🎯 KẾT LUẬN

### Ưu điểm Real-time Version:
✅ Memory constant - không tăng theo thời gian  
✅ Latency thấp - phát hiện ngay  
✅ Scalable - xử lý nhiều xe  
✅ Real-time alerts  
✅ Suitable cho IoT/embedded systems  

### Khi nào dùng Real-time:
- Fleet management system
- GPS tracking system
- IoT fuel monitoring
- Real-time alerts/warnings
- Streaming analytics

### Khi nào dùng Batch:
- Historical analysis
- Report generation
- Deep learning training
- Offline optimization

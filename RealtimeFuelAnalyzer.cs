using System;
using System.Collections.Generic;
using System.Linq;

namespace FuelAnalysisSystem.Realtime
{
    /// <summary>
    /// Dữ liệu xe theo thời gian
    /// </summary>
    public class VehicleDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Velocity { get; set; }        // km/h
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double FuelLevel { get; set; }       // Lít
    }

    /// <summary>
    /// Segment đã phát hiện (đổ, hút, hoặc tiêu hao)
    /// </summary>
    public class FuelSegment
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public SegmentType Type { get; set; }
        public double FuelChange { get; set; }      // (+) đổ, (-) hút/tiêu hao
        public double Distance { get; set; }        // km
        public double AverageVelocity { get; set; } // km/h
        public GeoLocation StartLocation { get; set; }
        public GeoLocation EndLocation { get; set; }
        public double Confidence { get; set; }      // 0-1
        public bool IsComplete { get; set; }        // Segment đã kết thúc chưa
        
        public TimeSpan Duration => EndTime - StartTime;
        
        public override string ToString()
        {
            string typeStr = Type switch
            {
                SegmentType.Refueling => "ĐỔ NHIÊN LIỆU",
                SegmentType.Theft => "HÚT/TRỘM",
                SegmentType.NormalConsumption => "Tiêu hao bình thường",
                SegmentType.Idle => "Đứng yên",
                _ => "Không xác định"
            };
            
            string status = IsComplete ? "✓" : "...";
            
            return $"{status} [{StartTime:HH:mm:ss} → {EndTime:HH:mm:ss}] {typeStr}: " +
                   $"{Math.Abs(FuelChange):F2}L | " +
                   $"V: {AverageVelocity:F1}km/h | " +
                   $"D: {Distance:F2}km | " +
                   $"Conf: {Confidence:P0}";
        }
    }

    public enum SegmentType
    {
        NormalConsumption,  // Tiêu hao bình thường
        Refueling,          // Đổ nhiên liệu
        Theft,              // Hút/trộm nhiên liệu
        Idle,               // Xe đứng yên
        Unknown             // Chưa xác định
    }

    public class GeoLocation
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        
        public double DistanceTo(GeoLocation other)
        {
            const double R = 6371;
            double dLat = ToRadians(other.Latitude - Latitude);
            double dLon = ToRadians(other.Longitude - Longitude);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }
        
        private double ToRadians(double degrees) => degrees * Math.PI / 180;
    }

    /// <summary>
    /// HỆ THỐNG PHÂN TÍCH REAL-TIME - XỬ LÝ TỪNG BẢN TIN
    /// </summary>
    public class RealtimeFuelAnalyzer
    {
        // Filters cho làm mượt
        private readonly ButterworthFilter butterworthFilter;
        private readonly KalmanFilter kalmanFilter;
        
        // State management
        private readonly RollingWindow<ProcessedDataPoint> dataWindow;
        private readonly List<FuelSegment> completedSegments;
        private FuelSegment currentSegment;
        
        // Configuration
        private readonly double normalConsumptionRate;  // L/km
        private readonly double velocityThreshold;      // km/h - ngưỡng xe đang chạy
        
        // Thresholds
        private const double REFUEL_THRESHOLD = 3.0;
        private const double THEFT_THRESHOLD = 2.0;
        private const int WINDOW_SIZE = 10;              // Giữ 10 data points gần nhất
        private const double SEGMENT_TIMEOUT_MINUTES = 5; // Timeout để đóng segment
        
        // Events
        public event EventHandler<FuelSegment> OnSegmentCompleted;
        public event EventHandler<ProcessedDataPoint> OnDataProcessed;
        
        public RealtimeFuelAnalyzer(
            double normalConsumptionRate = 0.08,  // 8L/100km
            double velocityThreshold = 5.0,       // km/h
            double samplingRate = 1.0)            // Hz
        {
            this.normalConsumptionRate = normalConsumptionRate;
            this.velocityThreshold = velocityThreshold;
            
            // Khởi tạo filters
            butterworthFilter = new ButterworthFilter(
                order: 2,
                cutoffFrequency: 0.1,
                samplingRate: samplingRate
            );
            
            kalmanFilter = new KalmanFilter(
                processNoise: 0.01,
                measurementNoise: 0.5
            );
            
            dataWindow = new RollingWindow<ProcessedDataPoint>(WINDOW_SIZE);
            completedSegments = new List<FuelSegment>();
            currentSegment = null;
        }
        
        /// <summary>
        /// MAIN METHOD: Xử lý từng bản tin realtime
        /// </summary>
        public ProcessedDataPoint ProcessDataPoint(VehicleDataPoint rawData)
        {
            // Bước 1: Làm mượt dữ liệu
            double butterworthFiltered = butterworthFilter.Filter(rawData.FuelLevel);
            double smoothedFuel = kalmanFilter.Update(butterworthFiltered);
            
            var processedPoint = new ProcessedDataPoint
            {
                Timestamp = rawData.Timestamp,
                Velocity = rawData.Velocity,
                Latitude = rawData.Latitude,
                Longitude = rawData.Longitude,
                RawFuelLevel = rawData.FuelLevel,
                SmoothedFuelLevel = smoothedFuel
            };
            
            // Bước 2: Thêm vào window
            dataWindow.Add(processedPoint);
            
            // Bước 3: Phân tích segment (nếu có đủ dữ liệu)
            if (dataWindow.Count >= 2)
            {
                AnalyzeAndUpdateSegment();
            }
            
            // Bước 4: Kiểm tra timeout segment
            CheckSegmentTimeout(rawData.Timestamp);
            
            // Trigger event
            OnDataProcessed?.Invoke(this, processedPoint);
            
            return processedPoint;
        }
        
        /// <summary>
        /// Phân tích và cập nhật segment hiện tại
        /// </summary>
        private void AnalyzeAndUpdateSegment()
        {
            var previous = dataWindow.GetFromEnd(1); // Data point trước
            var current = dataWindow.GetFromEnd(0);  // Data point hiện tại
            
            if (previous == null || current == null) return;
            
            // Tính toán các metrics
            double fuelChange = current.SmoothedFuelLevel - previous.SmoothedFuelLevel;
            double timeDeltaHours = (current.Timestamp - previous.Timestamp).TotalHours;
            
            var prevLoc = new GeoLocation { Latitude = previous.Latitude, Longitude = previous.Longitude };
            var currLoc = new GeoLocation { Latitude = current.Latitude, Longitude = current.Longitude };
            double distance = prevLoc.DistanceTo(currLoc);
            
            bool isMoving = current.Velocity > velocityThreshold;
            
            // Xác định loại event
            SegmentType detectedType = ClassifyEvent(fuelChange, isMoving, current.Velocity);
            
            // Quyết định: Tiếp tục segment hiện tại hay bắt đầu segment mới?
            if (currentSegment == null)
            {
                // Bắt đầu segment mới
                StartNewSegment(previous, detectedType);
            }
            else if (ShouldContinueCurrentSegment(detectedType, fuelChange, isMoving))
            {
                // Tiếp tục segment hiện tại
                UpdateCurrentSegment(current, fuelChange, distance);
            }
            else
            {
                // Kết thúc segment hiện tại và bắt đầu segment mới
                CompleteCurrentSegment(previous);
                StartNewSegment(previous, detectedType);
            }
        }
        
        /// <summary>
        /// Phân loại event dựa trên fuel change và velocity
        /// </summary>
        private SegmentType ClassifyEvent(double fuelChange, bool isMoving, double velocity)
        {
            // ĐỔ NHIÊN LIỆU: Tăng đột ngột
            if (fuelChange > REFUEL_THRESHOLD)
                return SegmentType.Refueling;
            
            // HÚT/TRỘM: Giảm đột ngột khi đứng yên
            if (fuelChange < -THEFT_THRESHOLD && !isMoving)
                return SegmentType.Theft;
            
            // TIÊU HAO BÌNH THƯỜNG: Xe đang chạy, giảm dần
            if (isMoving && fuelChange < 0)
                return SegmentType.NormalConsumption;
            
            // ĐỨNG YÊN: Xe không chạy, nhiên liệu không đổi
            if (!isMoving && Math.Abs(fuelChange) < 0.5)
                return SegmentType.Idle;
            
            return SegmentType.Unknown;
        }
        
        /// <summary>
        /// Kiểm tra có nên tiếp tục segment hiện tại không
        /// </summary>
        private bool ShouldContinueCurrentSegment(SegmentType newType, double fuelChange, bool isMoving)
        {
            if (currentSegment == null) return false;
            
            // Nếu type khác nhau → segment mới
            if (currentSegment.Type != newType)
            {
                // Ngoại lệ: Idle có thể transition sang consumption
                if (currentSegment.Type == SegmentType.Idle && newType == SegmentType.NormalConsumption)
                    return false;
                    
                return false;
            }
            
            // Cùng type → kiểm tra consistency
            switch (currentSegment.Type)
            {
                case SegmentType.Refueling:
                    // Tiếp tục nếu vẫn đang tăng
                    return fuelChange > 0.5;
                    
                case SegmentType.Theft:
                    // Tiếp tục nếu vẫn đang giảm và đứng yên
                    return fuelChange < -0.5 && !isMoving;
                    
                case SegmentType.NormalConsumption:
                    // Tiếp tục nếu vẫn đang chạy
                    return isMoving;
                    
                case SegmentType.Idle:
                    // Tiếp tục nếu vẫn đứng yên
                    return !isMoving;
                    
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Bắt đầu segment mới
        /// </summary>
        private void StartNewSegment(ProcessedDataPoint startPoint, SegmentType type)
        {
            currentSegment = new FuelSegment
            {
                StartTime = startPoint.Timestamp,
                EndTime = startPoint.Timestamp,
                Type = type,
                FuelChange = 0,
                Distance = 0,
                AverageVelocity = startPoint.Velocity,
                StartLocation = new GeoLocation 
                { 
                    Latitude = startPoint.Latitude, 
                    Longitude = startPoint.Longitude 
                },
                EndLocation = new GeoLocation 
                { 
                    Latitude = startPoint.Latitude, 
                    Longitude = startPoint.Longitude 
                },
                IsComplete = false,
                Confidence = 0.5
            };
        }
        
        /// <summary>
        /// Cập nhật segment hiện tại với data point mới
        /// </summary>
        private void UpdateCurrentSegment(ProcessedDataPoint point, double fuelChange, double distance)
        {
            if (currentSegment == null) return;
            
            currentSegment.EndTime = point.Timestamp;
            currentSegment.FuelChange += fuelChange;
            currentSegment.Distance += distance;
            currentSegment.EndLocation = new GeoLocation 
            { 
                Latitude = point.Latitude, 
                Longitude = point.Longitude 
            };
            
            // Cập nhật average velocity
            var duration = currentSegment.Duration.TotalHours;
            if (duration > 0 && currentSegment.Distance > 0)
            {
                currentSegment.AverageVelocity = currentSegment.Distance / duration;
            }
            
            // Cập nhật confidence
            currentSegment.Confidence = CalculateConfidence(currentSegment);
        }
        
        /// <summary>
        /// Hoàn thành segment hiện tại
        /// </summary>
        private void CompleteCurrentSegment(ProcessedDataPoint endPoint)
        {
            if (currentSegment == null) return;
            
            currentSegment.EndTime = endPoint.Timestamp;
            currentSegment.IsComplete = true;
            
            // Lưu vào danh sách completed
            completedSegments.Add(currentSegment);
            
            // Trigger event
            OnSegmentCompleted?.Invoke(this, currentSegment);
            
            // Reset
            currentSegment = null;
        }
        
        /// <summary>
        /// Kiểm tra timeout segment (segment quá lâu không update)
        /// </summary>
        private void CheckSegmentTimeout(DateTime currentTime)
        {
            if (currentSegment == null) return;
            
            var timeSinceLastUpdate = currentTime - currentSegment.EndTime;
            if (timeSinceLastUpdate.TotalMinutes > SEGMENT_TIMEOUT_MINUTES)
            {
                // Force complete segment
                currentSegment.IsComplete = true;
                completedSegments.Add(currentSegment);
                OnSegmentCompleted?.Invoke(this, currentSegment);
                currentSegment = null;
            }
        }
        
        /// <summary>
        /// Tính confidence cho segment
        /// </summary>
        private double CalculateConfidence(FuelSegment segment)
        {
            switch (segment.Type)
            {
                case SegmentType.Refueling:
                    // Đổ nhiều → confidence cao
                    double refuelConf = Math.Min(1.0, Math.Abs(segment.FuelChange) / 30.0);
                    // Penalty nếu average velocity cao (đang chạy)
                    if (segment.AverageVelocity > velocityThreshold)
                        refuelConf *= 0.5;
                    return refuelConf;
                    
                case SegmentType.Theft:
                    // Hút nhiều → confidence cao
                    double theftConf = Math.Min(1.0, Math.Abs(segment.FuelChange) / 20.0);
                    // Penalty nếu đang chạy
                    if (segment.AverageVelocity > velocityThreshold)
                        theftConf *= 0.3;
                    return theftConf;
                    
                case SegmentType.NormalConsumption:
                    // So sánh actual vs expected consumption
                    if (segment.Distance == 0) return 0.5;
                    double expected = segment.Distance * normalConsumptionRate;
                    double actual = Math.Abs(segment.FuelChange);
                    double ratio = actual / expected;
                    
                    if (ratio >= 0.8 && ratio <= 1.2) return 1.0;
                    if (ratio >= 0.6 && ratio <= 1.4) return 0.8;
                    if (ratio >= 0.4 && ratio <= 1.6) return 0.6;
                    return 0.3;
                    
                case SegmentType.Idle:
                    // Đứng yên, nhiên liệu không đổi nhiều
                    double idleConf = 1.0 - Math.Min(1.0, Math.Abs(segment.FuelChange) / 2.0);
                    return idleConf;
                    
                default:
                    return 0.3;
            }
        }
        
        /// <summary>
        /// Lấy segment hiện tại (chưa hoàn thành)
        /// </summary>
        public FuelSegment GetCurrentSegment()
        {
            return currentSegment;
        }
        
        /// <summary>
        /// Lấy tất cả segments đã hoàn thành
        /// </summary>
        public List<FuelSegment> GetCompletedSegments()
        {
            return new List<FuelSegment>(completedSegments);
        }
        
        /// <summary>
        /// Lấy các segments trong khoảng thời gian
        /// </summary>
        public List<FuelSegment> GetSegmentsByTimeRange(DateTime start, DateTime end)
        {
            return completedSegments
                .Where(s => s.StartTime >= start && s.EndTime <= end)
                .ToList();
        }
        
        /// <summary>
        /// Lấy segments theo loại
        /// </summary>
        public List<FuelSegment> GetSegmentsByType(SegmentType type)
        {
            return completedSegments
                .Where(s => s.Type == type)
                .ToList();
        }
        
        /// <summary>
        /// Reset toàn bộ state
        /// </summary>
        public void Reset()
        {
            dataWindow.Clear();
            completedSegments.Clear();
            currentSegment = null;
            kalmanFilter.Reset(0);
        }
        
        /// <summary>
        /// Lấy statistics tổng hợp
        /// </summary>
        public AnalysisStatistics GetStatistics()
        {
            var refuels = completedSegments.Where(s => s.Type == SegmentType.Refueling).ToList();
            var thefts = completedSegments.Where(s => s.Type == SegmentType.Theft).ToList();
            var consumptions = completedSegments.Where(s => s.Type == SegmentType.NormalConsumption).ToList();
            
            return new AnalysisStatistics
            {
                TotalSegments = completedSegments.Count,
                TotalRefuels = refuels.Count,
                TotalThefts = thefts.Count,
                TotalConsumptionSegments = consumptions.Count,
                TotalFuelRefueled = refuels.Sum(s => s.FuelChange),
                TotalFuelStolen = Math.Abs(thefts.Sum(s => s.FuelChange)),
                TotalFuelConsumed = Math.Abs(consumptions.Sum(s => s.FuelChange)),
                TotalDistance = consumptions.Sum(s => s.Distance),
                AverageConsumptionRate = consumptions.Sum(s => s.Distance) > 0 
                    ? Math.Abs(consumptions.Sum(s => s.FuelChange)) / consumptions.Sum(s => s.Distance) * 100 
                    : 0
            };
        }
    }

    #region Supporting Classes
    
    /// <summary>
    /// Data point đã xử lý (smoothed)
    /// </summary>
    public class ProcessedDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Velocity { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RawFuelLevel { get; set; }
        public double SmoothedFuelLevel { get; set; }
    }
    
    /// <summary>
    /// Statistics tổng hợp
    /// </summary>
    public class AnalysisStatistics
    {
        public int TotalSegments { get; set; }
        public int TotalRefuels { get; set; }
        public int TotalThefts { get; set; }
        public int TotalConsumptionSegments { get; set; }
        public double TotalFuelRefueled { get; set; }
        public double TotalFuelStolen { get; set; }
        public double TotalFuelConsumed { get; set; }
        public double TotalDistance { get; set; }
        public double AverageConsumptionRate { get; set; }
        
        public override string ToString()
        {
            return $"Segments: {TotalSegments} | " +
                   $"Refuels: {TotalRefuels} ({TotalFuelRefueled:F2}L) | " +
                   $"Thefts: {TotalThefts} ({TotalFuelStolen:F2}L) | " +
                   $"Consumption: {TotalFuelConsumed:F2}L / {TotalDistance:F2}km " +
                   $"({AverageConsumptionRate:F2}L/100km)";
        }
    }
    
    /// <summary>
    /// Rolling window để lưu N data points gần nhất
    /// </summary>
    public class RollingWindow<T>
    {
        private readonly Queue<T> queue;
        private readonly int maxSize;
        
        public RollingWindow(int maxSize)
        {
            this.maxSize = maxSize;
            queue = new Queue<T>(maxSize);
        }
        
        public void Add(T item)
        {
            if (queue.Count >= maxSize)
                queue.Dequeue();
            queue.Enqueue(item);
        }
        
        public T GetFromEnd(int offset)
        {
            if (offset >= queue.Count) return default(T);
            return queue.Reverse().Skip(offset).FirstOrDefault();
        }
        
        public int Count => queue.Count;
        
        public void Clear() => queue.Clear();
        
        public List<T> GetAll() => queue.ToList();
    }
    
    #endregion
    
    #region Filter Implementations
    
    public class ButterworthFilter
    {
        private readonly double[] b;
        private readonly double[] a;
        private readonly Queue<double> inputHistory;
        private readonly Queue<double> outputHistory;
        
        public ButterworthFilter(int order, double cutoffFrequency, double samplingRate)
        {
            double fc = cutoffFrequency / samplingRate;
            double wc = Math.Tan(Math.PI * fc);
            double wc2 = wc * wc;
            
            if (order == 2)
            {
                double sqrt2 = Math.Sqrt(2);
                double k1 = sqrt2 * wc;
                double k2 = wc2;
                double k3 = k1 + k2 + 1;
                
                b = new double[] { k2 / k3, 2 * k2 / k3, k2 / k3 };
                a = new double[] { 1, 2 * (k2 - 1) / k3, (1 + k2 - k1) / k3 };
            }
            else
            {
                double alpha = wc / (1 + wc);
                b = new double[] { alpha, alpha };
                a = new double[] { 1, -(1 - alpha) };
            }
            
            inputHistory = new Queue<double>(new double[b.Length]);
            outputHistory = new Queue<double>(new double[a.Length - 1]);
        }
        
        public double Filter(double input)
        {
            inputHistory.Enqueue(input);
            if (inputHistory.Count > b.Length)
                inputHistory.Dequeue();
            
            double output = 0;
            var inputArray = inputHistory.Reverse().ToArray();
            for (int i = 0; i < Math.Min(b.Length, inputArray.Length); i++)
                output += b[i] * inputArray[i];
            
            var outputArray = outputHistory.Reverse().ToArray();
            for (int i = 0; i < Math.Min(a.Length - 1, outputArray.Length); i++)
                output -= a[i + 1] * outputArray[i];
            
            outputHistory.Enqueue(output);
            if (outputHistory.Count > a.Length - 1)
                outputHistory.Dequeue();
            
            return output;
        }
    }
    
    public class KalmanFilter
    {
        private double x;
        private double P;
        private readonly double Q;
        private readonly double R;
        
        public KalmanFilter(double processNoise, double measurementNoise, double initialEstimate = 0)
        {
            Q = processNoise;
            R = measurementNoise;
            x = initialEstimate;
            P = 1.0;
        }
        
        public double Update(double measurement)
        {
            // Prediction
            double x_pred = x;
            double P_pred = P + Q;
            
            // Update
            double K = P_pred / (P_pred + R);
            x = x_pred + K * (measurement - x_pred);
            P = (1 - K) * P_pred;
            
            return x;
        }
        
        public void Reset(double initialValue)
        {
            x = initialValue;
            P = 1.0;
        }
    }
    
    #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace FuelAnalysisSystem.DocumentBased
{
    /// <summary>
    /// HỆ THỐNG PHÂN TÍCH NHIÊN LIỆU THEO TÀI LIỆU
    /// Dựa trên: Báo_cáo_ghi_nhận_cuốc_đổ_hút_-_New.mht
    /// </summary>
    public class DocumentBasedFuelAnalyzer
    {
        #region Configuration Parameters (from document)
        
        // b: Số giây mất tín hiệu
        public double LostSignalSeconds { get; set; } = 60;
        
        // c: % biến thiên khi tăng
        public double DeltaIncreasePercent { get; set; } = 5.0;
        
        // d: % biến thiên khi giảm
        public double AddLitsPercent { get; set; } = 5.0;
        
        // α (alpha): Hệ số lọc nhiễu (0-1)
        public double AlphaConstant { get; set; } = 0.3;
        
        // e: % đổ nhiên liệu để ghi nhận cuộc
        public double AddLitOfTripsPercent { get; set; } = 10.0;
        
        // f: % hút nhiên liệu để ghi nhận cuộc
        public double BringLitOfTripsPercent { get; set; } = 10.0;
        
        // g: Thời gian ổn định (giây)
        public double TimeStableSeconds { get; set; } = 300;
        
        // h: Số bản tin ổn định
        public int StableAfterCount { get; set; } = 10;
        
        // Vmin: Vận tốc tối thiểu coi là di chuyển
        public double Vmin { get; set; } = 5.0;
        
        // k: Số bản tin cho median filter
        public int MedianFilterCount { get; set; } = 3;
        
        // Dung tích bình (lít)
        public double TankCapacity { get; set; } = 100.0;
        
        // p: Ngưỡng xung mất kết nối
        public double PulseLostThreshold { get; set; } = 10.0;
        
        #endregion
        
        #region Internal State
        
        // BO: Danh sách các xung/lít ổn định (max k phần tử)
        private readonly List<StableReading> BO = new List<StableReading>();
        
        // BD: Danh sách xung/lít biến thiên
        private readonly List<VariationReading> BD = new List<VariationReading>();
        
        // Bảng điện áp (Voltage to Liter mapping)
        private readonly List<VoltagePoint> voltageTable = new List<VoltagePoint>();
        
        // Xung đã lọc trước đó (Pema)
        private double Pema = 0;
        
        // 2 xung chưa lọc gần nhất (cho median filter)
        private readonly Queue<double> rawPulseHistory = new Queue<double>(3);
        
        // Thời điểm bắt đầu ổn định
        private DateTime? timeStableStart = null;
        
        // Đếm số bản tin ổn định liên tiếp
        private int stableCount = 0;
        
        // Đếm số bản tin mất kết nối liên tiếp
        private int lostConnectionCount = 0;
        
        // Segments đã hoàn thành
        private readonly List<FuelSegmentDoc> completedSegments = new List<FuelSegmentDoc>();
        
        // Segment hiện tại (OverNight)
        private FuelSegmentDoc currentSegment = null;
        
        #endregion
        
        #region Events
        
        public event EventHandler<FuelSegmentDoc> OnSegmentCompleted;
        
        #endregion
        
        public DocumentBasedFuelAnalyzer()
        {
            InitializeDefaultVoltageTable();
        }
        
        /// <summary>
        /// BƯỚC 1: ĐẦU NGÀY - Load cấu hình và bảng điện áp
        /// </summary>
        public void InitializeDay(List<VoltagePoint> voltageMap = null)
        {
            if (voltageMap != null && voltageMap.Count > 0)
            {
                voltageTable.Clear();
                voltageTable.AddRange(voltageMap.OrderBy(v => v.Voltage));
            }
            
            // Load cuộc chờ xét từ OverNight (nếu có)
            // TODO: Load from database if needed
        }
        
        /// <summary>
        /// XỬ LÝ TỪNG BẢN TIN
        /// </summary>
        public void ProcessDataPoint(VehicleDataPointDoc dataPoint)
        {
            // BƯỚC 2: LỌC XUNG NHIỄU CƠ BẢN
            double filteredPulse = FilterNoise(dataPoint);
            
            if (filteredPulse == -1)
            {
                // Mất kết nối
                lostConnectionCount++;
                
                if (lostConnectionCount > 10)
                {
                    HandleConnectionLost(dataPoint);
                    return;
                }
            }
            else
            {
                lostConnectionCount = 0;
                
                // BƯỚC 3: ĐỔI XUNG SANG LÍT
                double liters = ConvertPulseToLiters(filteredPulse);
                
                // BƯỚC 4: TÍNH TOÁN BO/BD
                ProcessReading(dataPoint, filteredPulse, liters);
            }
        }
        
        /// <summary>
        /// BƯỚC 2: LỌC XUNG NHIỄU CƠ BẢN
        /// </summary>
        private double FilterNoise(VehicleDataPointDoc dataPoint)
        {
            double P = dataPoint.FuelPulse; // Xung hiện tại
            
            // Đổi xung sang điện áp: Vcur = P * 9900 / 4096
            double Vcur = P * 9900.0 / 4096.0;
            
            // Lấy ngưỡng từ bảng điện áp
            if (voltageTable.Count < 2)
            {
                return P; // Không có bảng điện áp
            }
            
            double Vmin = voltageTable.First().Voltage;
            double Vmax = voltageTable.Last().Voltage;
            
            // Kiểm tra vượt ngưỡng
            if (Vcur < Vmin * 0.9 || Vcur > Vmax * 1.1)
            {
                // Xung vượt ngưỡng → giữ nguyên Pema
                return Pema;
            }
            
            if (Vcur >= 0.9 * Vmin && Vcur < Vmin)
            {
                P = Vmin * 4096.0 / 9900.0;
            }
            else if (Vcur >= Vmax && Vcur < Vmax * 1.1)
            {
                P = Vmax * 4096.0 / 9900.0;
            }
            
            // Kiểm tra mất kết nối
            if (P < PulseLostThreshold)
            {
                return -1; // Mất tín hiệu
            }
            
            // Kiểm tra xe di chuyển
            if (dataPoint.Velocity > Vmin)
            {
                // Xe đang di chuyển
                // Nếu xung tăng → giữ xung trước
                if (P > Pema && Pema != 0)
                {
                    return Pema;
                }
            }
            
            // Áp dụng EMA filter với median
            rawPulseHistory.Enqueue(P);
            if (rawPulseHistory.Count > MedianFilterCount)
            {
                rawPulseHistory.Dequeue();
            }
            
            if (rawPulseHistory.Count >= MedianFilterCount)
            {
                // Tính median của 3 xung gần nhất
                var sorted = rawPulseHistory.OrderBy(x => x).ToList();
                double median = sorted[sorted.Count / 2];
                
                // Áp dụng EMA: Pema = α * median + (1-α) * Pema_prev
                if (Pema == 0)
                {
                    Pema = median;
                }
                else
                {
                    Pema = AlphaConstant * median + (1 - AlphaConstant) * Pema;
                }
            }
            else
            {
                Pema = P;
            }
            
            return Pema;
        }
        
        /// <summary>
        /// BƯỚC 3: ĐỔI XUNG SANG LÍT (Nội suy tuyến tính)
        /// </summary>
        private double ConvertPulseToLiters(double pulse)
        {
            if (voltageTable.Count < 2)
            {
                return pulse; // Không có bảng điện áp, trả về xung
            }
            
            // Đổi pulse sang voltage
            double voltage = pulse * 9900.0 / 4096.0;
            
            // Tìm 2 điểm gần nhất trong bảng điện áp
            VoltagePoint lower = null;
            VoltagePoint upper = null;
            
            for (int i = 0; i < voltageTable.Count - 1; i++)
            {
                if (voltage >= voltageTable[i].Voltage && voltage <= voltageTable[i + 1].Voltage)
                {
                    lower = voltageTable[i];
                    upper = voltageTable[i + 1];
                    break;
                }
            }
            
            if (lower == null || upper == null)
            {
                // Ngoài phạm vi, lấy điểm gần nhất
                if (voltage < voltageTable.First().Voltage)
                {
                    return voltageTable.First().Liters;
                }
                return voltageTable.Last().Liters;
            }
            
            // Nội suy tuyến tính
            double ratio = (voltage - lower.Voltage) / (upper.Voltage - lower.Voltage);
            double liters = lower.Liters + ratio * (upper.Liters - lower.Liters);
            
            return liters;
        }
        
        /// <summary>
        /// BƯỚC 4: TÍNH TOÁN BO/BD
        /// </summary>
        private void ProcessReading(VehicleDataPointDoc dataPoint, double pulse, double liters)
        {
            // Tính biến thiên
            double deltaL = 0;
            double deltaT = 0;
            
            if (BO.Count > 0)
            {
                var lastStable = BO.Last();
                deltaL = liters - lastStable.Liters;
                deltaT = (dataPoint.Timestamp - lastStable.Timestamp).TotalSeconds;
            }
            else if (BD.Count > 0)
            {
                var lastVariation = BD.Last();
                deltaL = liters - lastVariation.Liters;
                deltaT = (dataPoint.Timestamp - lastVariation.Timestamp).TotalSeconds;
            }
            
            // Tính ngưỡng ổn định
            double LD = (DeltaIncreasePercent / 100.0) * TankCapacity; // Ngưỡng tăng
            double LH = -(AddLitsPercent / 100.0) * TankCapacity;      // Ngưỡng giảm
            
            // Kiểm tra biến thiên ổn định
            bool isStable = (deltaL >= LH && deltaL <= LD);
            
            if (isStable)
            {
                // Biến thiên ổn định
                if (BD.Count > 0)
                {
                    // Có biến động trước đó → Kiểm tra đủ điều kiện kết thúc
                    stableCount++;
                    
                    if (timeStableStart == null)
                    {
                        timeStableStart = dataPoint.Timestamp;
                    }
                    
                    double stableTime = (dataPoint.Timestamp - timeStableStart.Value).TotalSeconds;
                    
                    // Đủ điều kiện ổn định
                    if (stableTime >= TimeStableSeconds || stableCount >= StableAfterCount)
                    {
                        // Kết thúc cuộc đổ/hút
                        CompleteSegment(dataPoint);
                        
                        // Chuyển k phần tử sang BO
                        BO.Clear();
                        BO.Add(new StableReading
                        {
                            Timestamp = dataPoint.Timestamp,
                            Pulse = pulse,
                            Liters = liters,
                            Latitude = dataPoint.Latitude,
                            Longitude = dataPoint.Longitude,
                            Km = dataPoint.Km
                        });
                        
                        stableCount = 0;
                        timeStableStart = null;
                    }
                }
                else
                {
                    // Chưa có biến động → Thêm vào BO
                    BO.Add(new StableReading
                    {
                        Timestamp = dataPoint.Timestamp,
                        Pulse = pulse,
                        Liters = liters,
                        Latitude = dataPoint.Latitude,
                        Longitude = dataPoint.Longitude,
                        Km = dataPoint.Km
                    });
                    
                    // Giữ tối đa k phần tử
                    while (BO.Count > StableAfterCount)
                    {
                        BO.RemoveAt(0);
                    }
                }
            }
            else
            {
                // Biến thiên ngoài ngưỡng
                if (BD.Count == 0)
                {
                    // Bắt đầu biến động mới
                    BD.Add(new VariationReading
                    {
                        Timestamp = dataPoint.Timestamp,
                        Pulse = pulse,
                        Liters = liters,
                        Latitude = dataPoint.Latitude,
                        Longitude = dataPoint.Longitude,
                        Km = dataPoint.Km
                    });
                }
                else
                {
                    // Tiếp tục biến động
                    if (stableCount > 0)
                    {
                        // Reset biến đếm ổn định
                        stableCount = 0;
                        timeStableStart = null;
                    }
                    
                    BD.Add(new VariationReading
                    {
                        Timestamp = dataPoint.Timestamp,
                        Pulse = pulse,
                        Liters = liters,
                        Latitude = dataPoint.Latitude,
                        Longitude = dataPoint.Longitude,
                        Km = dataPoint.Km
                    });
                }
            }
        }
        
        /// <summary>
        /// XỬ LÝ KHI MẤT KẾT NỐI
        /// </summary>
        private void HandleConnectionLost(VehicleDataPointDoc dataPoint)
        {
            if (BD.Count > 0)
            {
                CompleteSegment(dataPoint);
            }
        }
        
        /// <summary>
        /// BƯỚC 5: HOÀN THÀNH CUỘC ĐỔ/HÚT
        /// </summary>
        private void CompleteSegment(VehicleDataPointDoc endPoint)
        {
            if (BD.Count == 0)
                return;
            
            // Tính trung bình xung trong BO (k phần tử gần nhất)
            double PStb = BO.Count > 0 ? BO.Average(x => x.Pulse) : 0;
            double LStb = BO.Count > 0 ? BO.Average(x => x.Liters) : 0;
            
            // Tính trung bình xung trong BD
            double PEtb = BD.Average(x => x.Pulse);
            double LEtb = BD.Average(x => x.Liters);
            
            // Tính biến thiên
            double La = LEtb - LStb;
            
            // Xác định loại
            SegmentTypeDoc segmentType = SegmentTypeDoc.Unknown;
            bool isSuspicious = (BD.Count == 1); // Nghi ngờ nếu chỉ có 1 bản tin
            
            double eThreshold = (AddLitOfTripsPercent / 100.0) * TankCapacity;
            double fThreshold = (BringLitOfTripsPercent / 100.0) * TankCapacity;
            
            if (La >= eThreshold)
            {
                segmentType = isSuspicious ? SegmentTypeDoc.SuspectedRefueling : SegmentTypeDoc.Refueling;
            }
            else if (La <= -fThreshold)
            {
                segmentType = isSuspicious ? SegmentTypeDoc.SuspectedTheft : SegmentTypeDoc.Theft;
            }
            else
            {
                // Không đủ ngưỡng → không ghi nhận
                BD.Clear();
                return;
            }
            
            // Tạo segment
            var segment = new FuelSegmentDoc
            {
                StartTime = BO.Count > 0 ? BO.Max(x => x.Timestamp) : BD.First().Timestamp,
                EndTime = endPoint.Timestamp,
                Type = segmentType,
                PreviousValue = PStb,
                AfterValue = PEtb,
                PreviousLit = LStb,
                AfterLit = LEtb,
                FuelChange = La,
                PreviousKm = BO.Count > 0 ? BO.Last().Km : BD.First().Km,
                AfterKm = endPoint.Km,
                Latitude = BO.Count > 0 ? BO.Last().Latitude : BD.First().Latitude,
                Longitude = BO.Count > 0 ? BO.Last().Longitude : BD.First().Longitude
            };
            
            completedSegments.Add(segment);
            OnSegmentCompleted?.Invoke(this, segment);
            
            // Reset BD
            BD.Clear();
        }
        
        /// <summary>
        /// BƯỚC 6: CUỐI NGÀY - Lưu OverNight
        /// </summary>
        public FuelSegmentDoc EndOfDay()
        {
            if (BD.Count > 0 && stableCount < StableAfterCount)
            {
                // Có cuộc chưa hoàn thành → Lưu OverNight
                currentSegment = new FuelSegmentDoc
                {
                    // Lưu state hiện tại
                    IsOverNight = true
                };
                
                return currentSegment;
            }
            
            return null;
        }
        
        /// <summary>
        /// Lấy danh sách segments đã hoàn thành
        /// </summary>
        public List<FuelSegmentDoc> GetCompletedSegments()
        {
            return new List<FuelSegmentDoc>(completedSegments);
        }
        
        private void InitializeDefaultVoltageTable()
        {
            // Bảng điện áp mẫu
            voltageTable.Add(new VoltagePoint { Voltage = 0, Liters = 0 });
            voltageTable.Add(new VoltagePoint { Voltage = 1000, Liters = 25 });
            voltageTable.Add(new VoltagePoint { Voltage = 2000, Liters = 50 });
            voltageTable.Add(new VoltagePoint { Voltage = 3000, Liters = 75 });
            voltageTable.Add(new VoltagePoint { Voltage = 4000, Liters = 100 });
        }
    }
    
    #region Data Models
    
    public class VehicleDataPointDoc
    {
        public DateTime Timestamp { get; set; }
        public double FuelPulse { get; set; }      // Xung nhiên liệu
        public double Velocity { get; set; }        // Vận tốc (km/h)
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Km { get; set; }              // Số km đã đi
    }
    
    public class StableReading
    {
        public DateTime Timestamp { get; set; }
        public double Pulse { get; set; }
        public double Liters { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Km { get; set; }
    }
    
    public class VariationReading
    {
        public DateTime Timestamp { get; set; }
        public double Pulse { get; set; }
        public double Liters { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Km { get; set; }
    }
    
    public class VoltagePoint
    {
        public double Voltage { get; set; }  // mV
        public double Liters { get; set; }
    }
    
    public class FuelSegmentDoc
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public SegmentTypeDoc Type { get; set; }
        public double PreviousValue { get; set; }   // Xung bắt đầu
        public double AfterValue { get; set; }      // Xung kết thúc
        public double PreviousLit { get; set; }     // Lít bắt đầu
        public double AfterLit { get; set; }        // Lít kết thúc
        public double FuelChange { get; set; }      // La
        public double PreviousKm { get; set; }
        public double AfterKm { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsOverNight { get; set; }       // Cuộc chưa hoàn thành
        
        public override string ToString()
        {
            string typeStr = Type switch
            {
                SegmentTypeDoc.Refueling => "ĐỔ NHIÊN LIỆU",
                SegmentTypeDoc.Theft => "HÚT/TRỘM",
                SegmentTypeDoc.SuspectedRefueling => "NGHI NGỜ ĐỔ",
                SegmentTypeDoc.SuspectedTheft => "NGHI NGỜ HÚT",
                _ => "Không xác định"
            };
            
            return $"[{StartTime:HH:mm:ss} → {EndTime:HH:mm:ss}] {typeStr}: " +
                   $"{Math.Abs(FuelChange):F2}L | " +
                   $"Pulse: {PreviousValue:F0} → {AfterValue:F0}";
        }
    }
    
    public enum SegmentTypeDoc
    {
        Unknown,
        Refueling,              // Đổ (La ≥ e)
        Theft,                  // Hút (La ≤ -f)
        SuspectedRefueling,     // Nghi ngờ đổ (BD = 1)
        SuspectedTheft          // Nghi ngờ hút (BD = 1)
    }
    
    #endregion
}

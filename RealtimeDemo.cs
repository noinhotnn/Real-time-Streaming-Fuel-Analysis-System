using System;
using System.Collections.Generic;
using System.Threading;
using FuelAnalysisSystem.Realtime;

namespace FuelAnalysisDemo
{
    /// <summary>
    /// DEMO REAL-TIME FUEL ANALYSIS
    /// Mô phỏng dữ liệu xe truyền từng bản tin realtime
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║        REAL-TIME FUEL ANALYSIS SYSTEM - DEMO                  ║");
            Console.WriteLine("║        Xử lý từng bản tin xe gửi về                           ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝\n");
            
            // Khởi tạo analyzer
            var analyzer = new RealtimeFuelAnalyzer(
                normalConsumptionRate: 0.08,  // 8L/100km
                velocityThreshold: 5.0,       // 5km/h
                samplingRate: 1.0             // 1Hz
            );
            
            // Đăng ký events
            analyzer.OnSegmentCompleted += OnSegmentDetected;
            analyzer.OnDataProcessed += OnDataReceived;
            
            Console.WriteLine("Bắt đầu nhận dữ liệu từng bản tin...\n");
            Console.WriteLine(new string('═', 80));
            
            // Chạy các scenarios
            RunScenario1_NormalDriving(analyzer);
            Thread.Sleep(1000);
            
            RunScenario2_Refueling(analyzer);
            Thread.Sleep(1000);
            
            RunScenario3_Theft(analyzer);
            Thread.Sleep(1000);
            
            RunScenario4_MixedScenario(analyzer);
            
            // Hiển thị tổng kết
            Console.WriteLine("\n" + new string('═', 80));
            DisplayStatistics(analyzer);
            
            Console.WriteLine("\nNhấn phím bất kỳ để thoát...");
            Console.ReadKey();
        }
        
        /// <summary>
        /// Scenario 1: Xe chạy bình thường
        /// </summary>
        static void RunScenario1_NormalDriving(RealtimeFuelAnalyzer analyzer)
        {
            Console.WriteLine("\n🚗 SCENARIO 1: Xe chạy bình thường 10km\n");
            
            var simulator = new VehicleSimulator(
                initialFuel: 100.0,
                initialLat: 21.028511,
                initialLon: 105.804817
            );
            
            for (int i = 0; i < 20; i++)
            {
                var data = simulator.GenerateNormalDriving(
                    velocity: 60,        // 60 km/h
                    durationSeconds: 30  // 30 giây
                );
                
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100); // Mô phỏng delay giữa các bản tin
            }
        }
        
        /// <summary>
        /// Scenario 2: Đổ nhiên liệu
        /// </summary>
        static void RunScenario2_Refueling(RealtimeFuelAnalyzer analyzer)
        {
            Console.WriteLine("\n⛽ SCENARIO 2: Dừng đổ nhiên liệu\n");
            
            var simulator = new VehicleSimulator(
                initialFuel: 85.0,
                initialLat: 21.035511,
                initialLon: 105.814817
            );
            
            // Dừng xe
            for (int i = 0; i < 3; i++)
            {
                var data = simulator.GenerateIdle();
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
            
            // Đổ nhiên liệu (tăng dần)
            for (int i = 0; i < 10; i++)
            {
                var data = simulator.GenerateRefueling(amountPerStep: 2.5);
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
            
            // Hoàn thành đổ
            for (int i = 0; i < 3; i++)
            {
                var data = simulator.GenerateIdle();
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
        }
        
        /// <summary>
        /// Scenario 3: Trộm nhiên liệu
        /// </summary>
        static void RunScenario3_Theft(RealtimeFuelAnalyzer analyzer)
        {
            Console.WriteLine("\n🚨 SCENARIO 3: Phát hiện hút/trộm nhiên liệu\n");
            
            var simulator = new VehicleSimulator(
                initialFuel: 100.0,
                initialLat: 21.025511,
                initialLon: 105.824817
            );
            
            // Xe dừng lại
            for (int i = 0; i < 3; i++)
            {
                var data = simulator.GenerateIdle();
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
            
            // Hút nhiên liệu (giảm đột ngột)
            for (int i = 0; i < 8; i++)
            {
                var data = simulator.GenerateTheft(amountPerStep: 2.0);
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
            
            // Xe tiếp tục đứng
            for (int i = 0; i < 5; i++)
            {
                var data = simulator.GenerateIdle();
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(100);
            }
        }
        
        /// <summary>
        /// Scenario 4: Kịch bản hỗn hợp thực tế
        /// </summary>
        static void RunScenario4_MixedScenario(RealtimeFuelAnalyzer analyzer)
        {
            Console.WriteLine("\n🔄 SCENARIO 4: Kịch bản hỗn hợp (chạy → dừng → đổ → chạy)\n");
            
            var simulator = new VehicleSimulator(
                initialFuel: 70.0,
                initialLat: 21.030000,
                initialLon: 105.850000
            );
            
            // 1. Chạy một đoạn
            Console.WriteLine("  Phase 1: Chạy 5km...");
            for (int i = 0; i < 10; i++)
            {
                var data = simulator.GenerateNormalDriving(velocity: 50, durationSeconds: 30);
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(50);
            }
            
            // 2. Dừng lại
            Console.WriteLine("  Phase 2: Dừng xe...");
            for (int i = 0; i < 5; i++)
            {
                var data = simulator.GenerateIdle();
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(50);
            }
            
            // 3. Đổ nhiên liệu
            Console.WriteLine("  Phase 3: Đổ nhiên liệu...");
            for (int i = 0; i < 12; i++)
            {
                var data = simulator.GenerateRefueling(amountPerStep: 2.0);
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(50);
            }
            
            // 4. Tiếp tục chạy
            Console.WriteLine("  Phase 4: Tiếp tục chạy...");
            for (int i = 0; i < 15; i++)
            {
                var data = simulator.GenerateNormalDriving(velocity: 70, durationSeconds: 30);
                analyzer.ProcessDataPoint(data);
                Thread.Sleep(50);
            }
        }
        
        /// <summary>
        /// Event handler: Khi nhận được data point mới
        /// </summary>
        static void OnDataReceived(object sender, ProcessedDataPoint data)
        {
            // In ra màn hình mỗi 5 data points
            // (để không spam console)
            if (new Random().Next(5) == 0)
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine($"  📍 [{data.Timestamp:HH:mm:ss}] " +
                                $"V={data.Velocity:F0}km/h | " +
                                $"Raw={data.RawFuelLevel:F2}L → " +
                                $"Smooth={data.SmoothedFuelLevel:F2}L");
                Console.ResetColor();
            }
        }
        
        /// <summary>
        /// Event handler: Khi phát hiện segment hoàn chỉnh
        /// </summary>
        static void OnSegmentDetected(object sender, FuelSegment segment)
        {
            Console.ForegroundColor = GetColorForSegmentType(segment.Type);
            Console.WriteLine($"\n  ✓ SEGMENT DETECTED: {segment}\n");
            Console.ResetColor();
        }
        
        static ConsoleColor GetColorForSegmentType(SegmentType type)
        {
            return type switch
            {
                SegmentType.Refueling => ConsoleColor.Green,
                SegmentType.Theft => ConsoleColor.Red,
                SegmentType.NormalConsumption => ConsoleColor.Cyan,
                SegmentType.Idle => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };
        }
        
        /// <summary>
        /// Hiển thị statistics
        /// </summary>
        static void DisplayStatistics(RealtimeFuelAnalyzer analyzer)
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    TỔNG KẾT PHÂN TÍCH                         ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝\n");
            
            var stats = analyzer.GetStatistics();
            Console.WriteLine(stats);
            
            Console.WriteLine("\n═══ CHI TIẾT CÁC SEGMENTS ═══\n");
            
            var completedSegments = analyzer.GetCompletedSegments();
            foreach (var segment in completedSegments)
            {
                Console.ForegroundColor = GetColorForSegmentType(segment.Type);
                Console.WriteLine(segment);
                Console.ResetColor();
            }
            
            // Current segment (nếu có)
            var currentSegment = analyzer.GetCurrentSegment();
            if (currentSegment != null)
            {
                Console.WriteLine("\n⏳ SEGMENT HIỆN TẠI (đang xử lý):");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(currentSegment);
                Console.ResetColor();
            }
            
            // Phân loại theo type
            Console.WriteLine("\n═══ THỐNG KÊ THEO LOẠI ═══\n");
            
            var refuels = analyzer.GetSegmentsByType(SegmentType.Refueling);
            var thefts = analyzer.GetSegmentsByType(SegmentType.Theft);
            var consumptions = analyzer.GetSegmentsByType(SegmentType.NormalConsumption);
            var idles = analyzer.GetSegmentsByType(SegmentType.Idle);
            
            Console.WriteLine($"🟢 Đổ nhiên liệu:        {refuels.Count} lần, tổng {stats.TotalFuelRefueled:F2}L");
            Console.WriteLine($"🔴 Hút/Trộm:             {thefts.Count} lần, tổng {stats.TotalFuelStolen:F2}L");
            Console.WriteLine($"🔵 Tiêu hao bình thường: {consumptions.Count} đoạn, tổng {stats.TotalFuelConsumed:F2}L");
            Console.WriteLine($"🟡 Đứng yên:             {idles.Count} đoạn");
            
            if (stats.TotalDistance > 0)
            {
                Console.WriteLine($"\n📊 Mức tiêu hao TB: {stats.AverageConsumptionRate:F2}L/100km");
                Console.WriteLine($"📏 Tổng quãng đường: {stats.TotalDistance:F2}km");
            }
        }
    }
    
    /// <summary>
    /// Simulator để tạo dữ liệu giả lập
    /// </summary>
    public class VehicleSimulator
    {
        private double currentFuel;
        private double currentLat;
        private double currentLon;
        private DateTime currentTime;
        private Random random;
        
        private const double SENSOR_NOISE = 0.3;      // Nhiễu cảm biến ±0.3L
        private const double CONSUMPTION_RATE = 0.08; // 8L/100km
        
        public VehicleSimulator(double initialFuel, double initialLat, double initialLon)
        {
            currentFuel = initialFuel;
            currentLat = initialLat;
            currentLon = initialLon;
            currentTime = DateTime.Now;
            random = new Random();
        }
        
        /// <summary>
        /// Tạo data khi xe chạy bình thường
        /// </summary>
        public VehicleDataPoint GenerateNormalDriving(double velocity, int durationSeconds)
        {
            currentTime = currentTime.AddSeconds(durationSeconds);
            
            // Tính quãng đường
            double distance = velocity * durationSeconds / 3600.0; // km
            
            // Tiêu hao nhiên liệu
            double consumption = distance * CONSUMPTION_RATE;
            currentFuel -= consumption;
            
            // Cập nhật tọa độ (giả lập di chuyển)
            currentLat += distance * 0.001 * (random.NextDouble() - 0.5);
            currentLon += distance * 0.001 * (random.NextDouble() - 0.5);
            
            // Thêm noise
            double noise = (random.NextDouble() - 0.5) * SENSOR_NOISE * 2;
            
            return new VehicleDataPoint
            {
                Timestamp = currentTime,
                Velocity = velocity + (random.NextDouble() - 0.5) * 5, // Noise vận tốc
                Latitude = currentLat,
                Longitude = currentLon,
                FuelLevel = currentFuel + noise
            };
        }
        
        /// <summary>
        /// Tạo data khi xe đứng yên
        /// </summary>
        public VehicleDataPoint GenerateIdle()
        {
            currentTime = currentTime.AddSeconds(10);
            
            // Xe đứng yên, nhiên liệu không đổi (chỉ có noise)
            double noise = (random.NextDouble() - 0.5) * SENSOR_NOISE;
            
            return new VehicleDataPoint
            {
                Timestamp = currentTime,
                Velocity = 0,
                Latitude = currentLat,
                Longitude = currentLon,
                FuelLevel = currentFuel + noise
            };
        }
        
        /// <summary>
        /// Tạo data khi đổ nhiên liệu
        /// </summary>
        public VehicleDataPoint GenerateRefueling(double amountPerStep)
        {
            currentTime = currentTime.AddSeconds(5);
            currentFuel += amountPerStep;
            
            double noise = (random.NextDouble() - 0.5) * SENSOR_NOISE;
            
            return new VehicleDataPoint
            {
                Timestamp = currentTime,
                Velocity = 0,
                Latitude = currentLat,
                Longitude = currentLon,
                FuelLevel = currentFuel + noise
            };
        }
        
        /// <summary>
        /// Tạo data khi bị hút/trộm nhiên liệu
        /// </summary>
        public VehicleDataPoint GenerateTheft(double amountPerStep)
        {
            currentTime = currentTime.AddSeconds(5);
            currentFuel -= amountPerStep;
            
            double noise = (random.NextDouble() - 0.5) * SENSOR_NOISE * 0.5; // Noise nhỏ hơn
            
            return new VehicleDataPoint
            {
                Timestamp = currentTime,
                Velocity = 0,
                Latitude = currentLat,
                Longitude = currentLon,
                FuelLevel = currentFuel + noise
            };
        }
    }
}

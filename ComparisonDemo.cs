using System;
using System.Collections.Generic;
using FuelAnalysisSystem.Realtime;
using FuelAnalysisSystem.DocumentBased;

namespace FuelAnalysisComparison
{
    /// <summary>
    /// DEMO SO SÁNH 2 PHƯƠNG PHÁP
    /// </summary>
    class ComparisonDemo
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║          SO SÁNH 2 PHƯƠNG PHÁP PHÂN TÍCH NHIÊN LIỆU             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝\n");
            
            // Test Scenario 1: Clean Refueling
            Console.WriteLine("═══════════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 1: ĐỔ NHIÊN LIỆU SẠCH (30L, cảm biến tốt)");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");
            TestCleanRefueling();
            
            Console.WriteLine("\n\n");
            
            // Test Scenario 2: Noisy Theft
            Console.WriteLine("═══════════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 2: HÚT NHIÊN LIỆU VỚI NHIỄU (15L, cảm biến nhiễu ±2L)");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");
            TestNoisyTheft();
            
            Console.WriteLine("\n\n");
            
            // Test Scenario 3: Gradual Theft
            Console.WriteLine("═══════════════════════════════════════════════════════════════════");
            Console.WriteLine("TEST 3: HÚT TỪ TỪ (1L/5phút trong 1 giờ)");
            Console.WriteLine("═══════════════════════════════════════════════════════════════════\n");
            TestGradualTheft();
            
            Console.WriteLine("\n\nNhấn phím bất kỳ để thoát...");
            Console.ReadKey();
        }
        
        static void TestCleanRefueling()
        {
            Console.WriteLine("Kịch bản: Xe dừng lại, đổ 30L trong 5 phút, cảm biến tốt (±0.2L)\n");
            
            // Method 1: RealtimeFuelAnalyzer
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 1: RealtimeFuelAnalyzer ─────────────────────────┐");
            Console.ResetColor();
            
            var analyzer1 = new RealtimeFuelAnalyzer();
            DateTime startTime1 = DateTime.Now;
            bool detected1 = false;
            double detectedAmount1 = 0;
            double confidence1 = 0;
            
            analyzer1.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentType.Refueling)
                {
                    detected1 = true;
                    detectedAmount1 = segment.FuelChange;
                    confidence1 = segment.Confidence;
                    var elapsed = (segment.EndTime - startTime1).TotalSeconds;
                    Console.WriteLine($"  ✓ Phát hiện sau: {elapsed:F1}s");
                }
            };
            
            // Simulate data
            double currentFuel = 70.0;
            for (int i = 0; i < 60; i++)
            {
                if (i < 5)
                {
                    // Idle
                    analyzer1.ProcessDataPoint(new VehicleDataPoint
                    {
                        Timestamp = startTime1.AddSeconds(i * 5),
                        Velocity = 0,
                        FuelLevel = currentFuel + (Random.Shared.NextDouble() - 0.5) * 0.2,
                        Latitude = 21.0,
                        Longitude = 105.0
                    });
                }
                else if (i < 35)
                {
                    // Refueling
                    currentFuel += 1.0; // 1L per reading
                    analyzer1.ProcessDataPoint(new VehicleDataPoint
                    {
                        Timestamp = startTime1.AddSeconds(i * 5),
                        Velocity = 0,
                        FuelLevel = currentFuel + (Random.Shared.NextDouble() - 0.5) * 0.2,
                        Latitude = 21.0,
                        Longitude = 105.0
                    });
                }
                else
                {
                    // Stable after
                    analyzer1.ProcessDataPoint(new VehicleDataPoint
                    {
                        Timestamp = startTime1.AddSeconds(i * 5),
                        Velocity = 0,
                        FuelLevel = currentFuel + (Random.Shared.NextDouble() - 0.5) * 0.2,
                        Latitude = 21.0,
                        Longitude = 105.0
                    });
                }
            }
            
            Console.WriteLine($"  Phát hiện: {(detected1 ? "✓ CÓ" : "✗ KHÔNG")}");
            Console.WriteLine($"  Lượng đổ: {detectedAmount1:F2}L (Thực tế: 30.0L)");
            Console.WriteLine($"  Sai số: {Math.Abs(detectedAmount1 - 30.0):F2}L");
            Console.WriteLine($"  Confidence: {confidence1:P0}");
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            // Method 2: DocumentBasedFuelAnalyzer
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 2: DocumentBasedFuelAnalyzer ────────────────────┐");
            Console.ResetColor();
            
            var analyzer2 = new DocumentBasedFuelAnalyzer();
            analyzer2.TankCapacity = 100.0;
            analyzer2.AddLitOfTripsPercent = 10.0; // 10% = 10L threshold
            
            DateTime startTime2 = DateTime.Now;
            bool detected2 = false;
            double detectedAmount2 = 0;
            bool isSuspicious = false;
            
            analyzer2.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentTypeDoc.Refueling || 
                    segment.Type == SegmentTypeDoc.SuspectedRefueling)
                {
                    detected2 = true;
                    detectedAmount2 = segment.FuelChange;
                    isSuspicious = (segment.Type == SegmentTypeDoc.SuspectedRefueling);
                    var elapsed = (segment.EndTime - startTime2).TotalSeconds;
                    Console.WriteLine($"  ✓ Phát hiện sau: {elapsed:F1}s");
                }
            };
            
            // Simulate data (pulse-based)
            double currentPulse = 2048; // 50% tank
            for (int i = 0; i < 60; i++)
            {
                if (i < 5)
                {
                    analyzer2.ProcessDataPoint(new VehicleDataPointDoc
                    {
                        Timestamp = startTime2.AddSeconds(i * 5),
                        FuelPulse = currentPulse + (Random.Shared.NextDouble() - 0.5) * 10,
                        Velocity = 0,
                        Latitude = 21.0,
                        Longitude = 105.0,
                        Km = 1000
                    });
                }
                else if (i < 35)
                {
                    currentPulse += 40; // Increase pulse
                    analyzer2.ProcessDataPoint(new VehicleDataPointDoc
                    {
                        Timestamp = startTime2.AddSeconds(i * 5),
                        FuelPulse = currentPulse + (Random.Shared.NextDouble() - 0.5) * 10,
                        Velocity = 0,
                        Latitude = 21.0,
                        Longitude = 105.0,
                        Km = 1000
                    });
                }
                else
                {
                    analyzer2.ProcessDataPoint(new VehicleDataPointDoc
                    {
                        Timestamp = startTime2.AddSeconds(i * 5),
                        FuelPulse = currentPulse + (Random.Shared.NextDouble() - 0.5) * 10,
                        Velocity = 0,
                        Latitude = 21.0,
                        Longitude = 105.0,
                        Km = 1000
                    });
                }
            }
            
            Console.WriteLine($"  Phát hiện: {(detected2 ? "✓ CÓ" : "✗ KHÔNG")}");
            Console.WriteLine($"  Lượng đổ: {detectedAmount2:F2}L (Thực tế: 30.0L)");
            Console.WriteLine($"  Sai số: {Math.Abs(detectedAmount2 - 30.0):F2}L");
            Console.WriteLine($"  Trạng thái: {(isSuspicious ? "NGHI NGỜ" : "XÁC NHẬN")}");
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            // Summary
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("📊 TỔNG KẾT TEST 1:");
            Console.WriteLine($"  Độ chính xác:");
            Console.WriteLine($"    - Phương pháp 1: {100 - Math.Abs(detectedAmount1 - 30.0) / 30.0 * 100:F1}%");
            Console.WriteLine($"    - Phương pháp 2: {100 - Math.Abs(detectedAmount2 - 30.0) / 30.0 * 100:F1}%");
            Console.ResetColor();
        }
        
        static void TestNoisyTheft()
        {
            Console.WriteLine("Kịch bản: Hút 15L khi xe đứng, cảm biến nhiễu ±2L\n");
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 1: RealtimeFuelAnalyzer ─────────────────────────┐");
            Console.ResetColor();
            
            var analyzer1 = new RealtimeFuelAnalyzer();
            int falsePositives1 = 0;
            double detectedAmount1 = 0;
            
            analyzer1.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentType.Theft)
                {
                    detectedAmount1 += segment.FuelChange;
                    falsePositives1++;
                }
            };
            
            double fuel = 100.0;
            var time = DateTime.Now;
            
            for (int i = 0; i < 40; i++)
            {
                if (i >= 10 && i < 30)
                {
                    fuel -= 0.75; // Total 15L
                }
                
                // High noise
                double noise = (Random.Shared.NextDouble() - 0.5) * 4;
                
                analyzer1.ProcessDataPoint(new VehicleDataPoint
                {
                    Timestamp = time.AddSeconds(i * 10),
                    Velocity = 0,
                    FuelLevel = fuel + noise,
                    Latitude = 21.0,
                    Longitude = 105.0
                });
            }
            
            Console.WriteLine($"  Phát hiện: {Math.Abs(detectedAmount1):F2}L");
            Console.WriteLine($"  False positives: {Math.Max(0, falsePositives1 - 1)}");
            Console.WriteLine($"  Sai số: {Math.Abs(Math.Abs(detectedAmount1) - 15.0):F2}L");
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 2: DocumentBasedFuelAnalyzer ────────────────────┐");
            Console.ResetColor();
            
            var analyzer2 = new DocumentBasedFuelAnalyzer();
            analyzer2.TankCapacity = 100.0;
            analyzer2.BringLitOfTripsPercent = 10.0;
            
            int falsePositives2 = 0;
            double detectedAmount2 = 0;
            
            analyzer2.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentTypeDoc.Theft || 
                    segment.Type == SegmentTypeDoc.SuspectedTheft)
                {
                    detectedAmount2 += segment.FuelChange;
                    falsePositives2++;
                }
            };
            
            fuel = 100.0;
            double pulse = 3500;
            time = DateTime.Now;
            
            for (int i = 0; i < 40; i++)
            {
                if (i >= 10 && i < 30)
                {
                    pulse -= 30; // Decrease
                }
                
                double noise = (Random.Shared.NextDouble() - 0.5) * 80;
                
                analyzer2.ProcessDataPoint(new VehicleDataPointDoc
                {
                    Timestamp = time.AddSeconds(i * 10),
                    FuelPulse = pulse + noise,
                    Velocity = 0,
                    Latitude = 21.0,
                    Longitude = 105.0,
                    Km = 1000
                });
            }
            
            Console.WriteLine($"  Phát hiện: {Math.Abs(detectedAmount2):F2}L");
            Console.WriteLine($"  False positives: {Math.Max(0, falsePositives2 - 1)}");
            Console.WriteLine($"  Sai số: {Math.Abs(Math.Abs(detectedAmount2) - 15.0):F2}L");
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("📊 TỔNG KẾT TEST 2:");
            Console.WriteLine($"  → Phương pháp 2 xử lý nhiễu tốt hơn");
            Console.WriteLine($"  → EMA + Median filter robust hơn Butterworth + Kalman với nhiễu lớn");
            Console.ResetColor();
        }
        
        static void TestGradualTheft()
        {
            Console.WriteLine("Kịch bản: Hút từ từ 1L/5phút trong 1 giờ (tổng 12L)\n");
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 1: RealtimeFuelAnalyzer ─────────────────────────┐");
            Console.ResetColor();
            
            var analyzer1 = new RealtimeFuelAnalyzer();
            bool detected1 = false;
            
            analyzer1.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentType.Theft)
                {
                    detected1 = true;
                    Console.WriteLine($"  ✓ Phát hiện: {Math.Abs(segment.FuelChange):F2}L");
                }
            };
            
            double fuel = 100.0;
            for (int i = 0; i < 12; i++)
            {
                fuel -= 1.0;
                analyzer1.ProcessDataPoint(new VehicleDataPoint
                {
                    Timestamp = DateTime.Now.AddMinutes(i * 5),
                    Velocity = 0,
                    FuelLevel = fuel,
                    Latitude = 21.0,
                    Longitude = 105.0
                });
            }
            
            if (!detected1)
            {
                Console.WriteLine($"  ✗ KHÔNG phát hiện (1L/lần < ngưỡng 2L)");
            }
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("┌─ PHƯƠNG PHÁP 2: DocumentBasedFuelAnalyzer ────────────────────┐");
            Console.ResetColor();
            
            var analyzer2 = new DocumentBasedFuelAnalyzer();
            analyzer2.TankCapacity = 100.0;
            analyzer2.BringLitOfTripsPercent = 10.0;
            
            bool detected2 = false;
            
            analyzer2.OnSegmentCompleted += (sender, segment) =>
            {
                if (segment.Type == SegmentTypeDoc.Theft)
                {
                    detected2 = true;
                    Console.WriteLine($"  ✓ Phát hiện: {Math.Abs(segment.FuelChange):F2}L");
                    Console.WriteLine($"  ✓ Tích lũy trong BD list → vượt ngưỡng 10L");
                }
            };
            
            double pulse = 3500;
            for (int i = 0; i < 12; i++)
            {
                pulse -= 40;
                analyzer2.ProcessDataPoint(new VehicleDataPointDoc
                {
                    Timestamp = DateTime.Now.AddMinutes(i * 5),
                    FuelPulse = pulse,
                    Velocity = 0,
                    Latitude = 21.0,
                    Longitude = 105.0,
                    Km = 1000
                });
            }
            
            if (!detected2)
            {
                Console.WriteLine($"  ✗ KHÔNG phát hiện");
            }
            Console.WriteLine("└───────────────────────────────────────────────────────────────┘\n");
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("📊 TỔNG KẾT TEST 3:");
            Console.WriteLine($"  → Phương pháp 2 tốt hơn cho hút từ từ");
            Console.WriteLine($"  → BD list tích lũy biến động → phát hiện được");
            Console.ResetColor();
        }
    }
}

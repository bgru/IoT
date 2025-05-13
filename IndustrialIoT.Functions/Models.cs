using System;

namespace IndustrialIoT.Functions
{
    public class TelemetryMessage
    {
        public int DeviceId { get; set; }
        public string ProductionStatus { get; set; } = string.Empty;
        public string? WorkorderId { get; set; }
        public long GoodCount { get; set; }
        public long BadCount { get; set; }
        public double Temperature { get; set; }
        public DateTime Timestamp { get; set; }
        public string MessageType { get; set; } = "telemetry";
    }

    // Model dla error events z Agent (real-time EventHub)
    public class DeviceErrorEvent
    {
        public int DeviceId { get; set; }
        public string ErrorType { get; set; } = string.Empty; // "deviceError"
        public string Errors { get; set; } = string.Empty;    // "EmergencyStop, PowerFailure"
        public int ErrorCode { get; set; }                     // 15 (binary flags)
        public DateTime Timestamp { get; set; }
    }

    // Model dla error events z Stream Analytics (batch processing)
    public class DeviceErrorMessage
    {
        public int DeviceId { get; set; }
        public DeviceErrors Errors { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Model dla KPI danych z Stream Analytics
    public class ProductionKpiMessage
    {
        public int DeviceId { get; set; }
        public double GoodProductionPercentage { get; set; }
        public long TotalGoodCount { get; set; }
        public long TotalBadCount { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
    }

    // Enum zgodny z Agent kodem
    [Flags]
    public enum DeviceErrors
    {
        None = 0,
        EmergencyStop = 1,
        PowerFailure = 2,
        SensorFailure = 4,
        Unknown = 8
    }
}
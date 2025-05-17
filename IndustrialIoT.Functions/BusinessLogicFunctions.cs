using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IndustrialIoT.Functions
{
    public class BusinessLogicFunctions
    {
        private readonly IAzureIoTService _azureIoTService;

        // Track non-emergency stop errors per device (in production, use Table Storage or Redis)
        private static readonly Dictionary<int, Queue<DateTime>> _deviceErrors = new();

        public BusinessLogicFunctions(IAzureIoTService azureIoTService)
        {
            _azureIoTService = azureIoTService;
        }

        // Function triggered by IoT Hub messages via Event Hub
        [FunctionName("ProcessTelemetryData")]
        public async Task ProcessTelemetryData(
            [EventHubTrigger("messages/events", Connection = "IoTHubEventHubConnectionString")]
            string eventData,
            ILogger log)
        {
            try
            {
                log.LogInformation($"📨 Received message: {eventData}");

                // Parse message as dynamic to check message type
                var message = JsonConvert.DeserializeObject<dynamic>(eventData);
                if (message == null) return;

                // Determine message type from various possible properties
                string messageType = message.messageType ?? message.errorType ?? "unknown";

                switch (messageType.ToString().ToLower())
                {
                    case "telemetry":
                        await ProcessTelemetryMessage(eventData, log);
                        break;

                    case "error":
                    case "deviceerror":
                        await ProcessErrorMessage(eventData, log);
                        break;

                    default:
                        // Try to detect based on content
                        if (eventData.Contains("productionStatus") || eventData.Contains("temperature"))
                        {
                            await ProcessTelemetryMessage(eventData, log);
                        }
                        else if (eventData.Contains("errors") || eventData.Contains("errorCode"))
                        {
                            await ProcessErrorMessage(eventData, log);
                        }
                        else
                        {
                            log.LogWarning($"⚠️ Unknown message type: {messageType}");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "❌ Error processing EventHub message");
            }
        }

        private async Task ProcessTelemetryMessage(string eventData, ILogger log)
        {
            try
            {
                var telemetry = JsonConvert.DeserializeObject<TelemetryMessage>(eventData);
                if (telemetry == null) return;

                log.LogInformation($"📊 Processed telemetry for device {telemetry.DeviceId} - Status: {telemetry.ProductionStatus}, Temp: {telemetry.Temperature:F1}°C");

                // Additional telemetry logic can be added here
                // For example: temperature anomaly detection, production trend analysis, etc.
            }
            catch (Exception ex)
            {
                log.LogError(ex, "❌ Error processing telemetry message");
            }
        }

        private async Task ProcessErrorMessage(string eventData, ILogger log)
        {
            try
            {
                var errorMsg = JsonConvert.DeserializeObject<DeviceErrorEvent>(eventData);
                if (errorMsg == null) return;

                // Business Logic Rule 3: Send email alert for any device error
                log.LogWarning($"📧 EMAIL ALERT: Device {errorMsg.DeviceId} has errors: {errorMsg.Errors}");
                log.LogInformation($"   📋 Error Details: {GetErrorDescription((DeviceErrors)errorMsg.ErrorCode)}");
                log.LogInformation($"   🕐 Timestamp: {errorMsg.Timestamp:yyyy-MM-dd HH:mm:ss}");
                log.LogInformation($"   🔧 Action Required: Please check device {errorMsg.DeviceId} immediately");

                // Convert error code to DeviceErrors enum for further processing
                var deviceErrors = (DeviceErrors)errorMsg.ErrorCode;

                // Handle the error (track non-emergency stop errors for emergency stop logic)
                await HandleDeviceError(errorMsg.DeviceId, deviceErrors, log);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "❌ Error processing error message");
            }
        }

        // Function triggered by Stream Analytics output - Production KPI analysis
        [FunctionName("ProcessProductionKPI")]
        public async Task ProcessProductionKPI(
            [BlobTrigger("kpi-data/{name}", Connection = "StorageConnectionString")]
            string kpiData,
            string name,
            ILogger log)
        {
            try
            {
                log.LogInformation($"📈 Processing KPI data from {name}");

                // Process each line in the KPI data file
                var lines = kpiData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("{"))
                    {
                        try
                        {
                            var kpi = JsonConvert.DeserializeObject<ProductionKpiMessage>(line);
                            if (kpi == null) continue;

                            log.LogInformation($"📊 Device {kpi.DeviceId} KPI: {kpi.GoodProductionPercentage:F1}% efficiency");

                            // Business Logic Rule 2: Decrease production rate if efficiency < 90%
                            if (kpi.GoodProductionPercentage < 90.0)
                            {
                                log.LogWarning(
                                    $"📉 Device {kpi.DeviceId} efficiency is {kpi.GoodProductionPercentage:F1}% (< 90%) - decreasing production rate by 10 points");

                                await _azureIoTService.DecreaseProductionRateAsync(kpi.DeviceId);
                            }
                            else
                            {
                                log.LogInformation($"✅ Device {kpi.DeviceId} efficiency is acceptable: {kpi.GoodProductionPercentage:F1}%");
                            }
                        }
                        catch (JsonException ex)
                        {
                            log.LogWarning($"⚠️ Could not parse KPI line as JSON: {line}. Error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"❌ Error processing KPI data from {name}");
            }
        }

        // Function triggered by Stream Analytics error aggregation
        [FunctionName("ProcessDeviceErrors")]
        public async Task ProcessDeviceErrors(
            [BlobTrigger("error-data/{name}", Connection = "StorageConnectionString")]
            string errorData,
            string name,
            ILogger log)
        {
            try
            {
                log.LogInformation($"⚠️ Processing error aggregation data from {name}");

                var lines = errorData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var error = JsonConvert.DeserializeObject<DeviceErrorMessage>(line);
                        if (error == null) continue;

                        await HandleDeviceError(error.DeviceId, error.Errors, log);
                    }
                    catch (JsonException ex)
                    {
                        log.LogWarning($"⚠️ Could not parse error line as JSON: {line}. Error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"❌ Error processing error aggregation data from {name}");
            }
        }

        // Timer function for periodic maintenance and cleanup
        [FunctionName("PeriodicBusinessLogicCheck")]
        public async Task PeriodicBusinessLogicCheck(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
            ILogger log)
        {
            log.LogInformation("🔄 Running periodic business logic check");

            // Clean up old error records (older than 2 minutes)
            var cutoffTime = DateTime.UtcNow.AddMinutes(-2);
            var devicesWithErrors = _deviceErrors.Keys.ToList();

            foreach (var deviceId in devicesWithErrors)
            {
                if (_deviceErrors.ContainsKey(deviceId))
                {
                    var errorQueue = _deviceErrors[deviceId];
                    while (errorQueue.Count > 0 && errorQueue.Peek() < cutoffTime)
                    {
                        errorQueue.Dequeue();
                    }

                    // Remove empty queues to save memory
                    if (errorQueue.Count == 0)
                    {
                        _deviceErrors.Remove(deviceId);
                    }
                }
            }

            log.LogInformation($"✅ Periodic check completed. Tracking errors for {_deviceErrors.Count} devices");
            await Task.CompletedTask;
        }

        private async Task HandleDeviceError(int deviceId, DeviceErrors errors, ILogger log)
        {
            try
            {
                // Filter out EmergencyStop from error counting for business logic
                // EmergencyStop should not trigger another EmergencyStop
                var countableErrors = errors & ~DeviceErrors.EmergencyStop;

                if (countableErrors != DeviceErrors.None)
                {
                    // Initialize error tracking for device if not exists
                    if (!_deviceErrors.ContainsKey(deviceId))
                    {
                        _deviceErrors[deviceId] = new Queue<DateTime>();
                    }

                    // Add error timestamp (only for non-emergency stop errors)
                    _deviceErrors[deviceId].Enqueue(DateTime.UtcNow);

                    // Clean old errors (keep only last minute)
                    var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
                    while (_deviceErrors[deviceId].Count > 0 && _deviceErrors[deviceId].Peek() < oneMinuteAgo)
                    {
                        _deviceErrors[deviceId].Dequeue();
                    }

                    // Business Logic Rule 1: Emergency stop if more than 3 countable errors in 1 minute
                    var errorCount = _deviceErrors[deviceId].Count;

                    log.LogInformation($"📊 Device {deviceId} countable error count in last minute: {errorCount}");
                    log.LogInformation($"📋 Current errors: {errors} (countable: {countableErrors})");

                    if (errorCount > 3)
                    {
                        log.LogError($"🚨 EMERGENCY STOP TRIGGERED: Device {deviceId} has {errorCount} countable errors in last minute!");
                        log.LogError($"📧 URGENT EMAIL: Emergency stop triggered for device {deviceId} due to excessive errors");
                        log.LogInformation($"🔧 Triggering emergency stop for device {deviceId}...");

                        await _azureIoTService.TriggerEmergencyStopAsync(deviceId);
                    }
                    else
                    {
                        log.LogInformation($"ℹ️ Device {deviceId} error count ({errorCount}) is within acceptable limits (≤3)");
                    }
                }
                else
                {
                    log.LogInformation($"ℹ️ Device {deviceId} has only EmergencyStop error - not counting for emergency stop logic");
                }

                // Log all errors for email alerts (including EmergencyStop)
                if (errors != DeviceErrors.None)
                {
                    log.LogWarning($"📧 EMAIL NOTIFICATION: Device {deviceId} error status: {GetErrorDescription(errors)}");
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"❌ Error handling device error for device {deviceId}");
            }
        }

        private string GetErrorDescription(DeviceErrors errors)
        {
            if (errors == DeviceErrors.None)
                return "No errors";

            var descriptions = new List<string>();

            if (errors.HasFlag(DeviceErrors.EmergencyStop))
                descriptions.Add("Emergency Stop Active");
            if (errors.HasFlag(DeviceErrors.PowerFailure))
                descriptions.Add("Power Failure");
            if (errors.HasFlag(DeviceErrors.SensorFailure))
                descriptions.Add("Sensor Failure");
            if (errors.HasFlag(DeviceErrors.Unknown))
                descriptions.Add("Unknown Error");

            return string.Join(", ", descriptions);
        }
    }
}
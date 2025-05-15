using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace IndustrialIoT.Functions
{
    public interface IAzureIoTService
    {
        Task TriggerEmergencyStopAsync(int deviceId);
        Task DecreaseProductionRateAsync(int deviceId);
        Task ResetErrorStatusAsync(int deviceId);
    }

    public class AzureIoTService : IAzureIoTService, IDisposable
    {
        private readonly ServiceClient _serviceClient;
        private readonly RegistryManager _registryManager;
        private readonly string _deviceIdTemplate;
        private readonly ILogger<AzureIoTService> _logger;

        public AzureIoTService(ILogger<AzureIoTService> logger)
        {
            _logger = logger;

            var connectionString = Environment.GetEnvironmentVariable("IoTHubConnectionString")
                ?? throw new ArgumentException("IoTHubConnectionString not found in app settings");

            _serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
            _registryManager = RegistryManager.CreateFromConnectionString(connectionString);

            // Template for device naming - supports {id} placeholder
            _deviceIdTemplate = Environment.GetEnvironmentVariable("DeviceIdTemplate") ?? "Agent{id}";

            _logger.LogInformation("AzureIoTService initialized with template: {Template}", _deviceIdTemplate);
        }

        public async Task TriggerEmergencyStopAsync(int deviceId)
        {
            try
            {
                var deviceName = GetDeviceName(deviceId);
                _logger.LogInformation("Triggering emergency stop for device: {DeviceName}", deviceName);

                var method = new CloudToDeviceMethod("EmergencyStop");
                var payload = new { deviceId = deviceId };
                method.SetPayloadJson(JsonConvert.SerializeObject(payload));

                // Set timeout for method call
                method.ResponseTimeout = TimeSpan.FromSeconds(30);
                method.ConnectionTimeout = TimeSpan.FromSeconds(30);

                var result = await _serviceClient.InvokeDeviceMethodAsync(deviceName, method);

                if (result.Status == 200)
                {
                    _logger.LogInformation("✅ Emergency stop executed successfully on {DeviceName}", deviceName);
                }
                else
                {
                    _logger.LogError("❌ Emergency stop failed on {DeviceName} - Status: {Status}, Payload: {Payload}",
                        deviceName, result.Status, result.GetPayloadAsJson());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering emergency stop for device {DeviceId}", deviceId);
                throw; // Re-throw to let Function runtime handle it
            }
        }

        public async Task DecreaseProductionRateAsync(int deviceId)
        {
            try
            {
                var deviceName = GetDeviceName(deviceId);
                _logger.LogInformation("Decreasing production rate for device: {DeviceName}", deviceName);

                // Get current production rate from device twin
                var twin = await _registryManager.GetTwinAsync(deviceName);

                // Try different property names that might be used
                var currentRate = GetCurrentProductionRate(twin);

                // Calculate new rate (don't go below 0)
                var newRate = Math.Max(0, currentRate - 10);

                _logger.LogInformation("Current rate: {CurrentRate}%, New rate: {NewRate}% for {DeviceName}",
                    currentRate, newRate, deviceName);

                // Update desired properties
                twin.Properties.Desired["productionRate"] = newRate;
                await _registryManager.UpdateTwinAsync(twin.DeviceId, twin, twin.ETag);

                _logger.LogInformation("✅ Production rate decreased successfully for {DeviceName}: {CurrentRate}% → {NewRate}%",
                    deviceName, currentRate, newRate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error decreasing production rate for device {DeviceId}", deviceId);
                throw; // Re-throw to let Function runtime handle it
            }
        }

        public async Task ResetErrorStatusAsync(int deviceId)
        {
            try
            {
                var deviceName = GetDeviceName(deviceId);
                _logger.LogInformation("Resetting error status for device: {DeviceName}", deviceName);

                var method = new CloudToDeviceMethod("ResetErrorStatus");
                var payload = new { deviceId = deviceId };
                method.SetPayloadJson(JsonConvert.SerializeObject(payload));

                // Set timeout for method call
                method.ResponseTimeout = TimeSpan.FromSeconds(30);
                method.ConnectionTimeout = TimeSpan.FromSeconds(30);

                var result = await _serviceClient.InvokeDeviceMethodAsync(deviceName, method);

                if (result.Status == 200)
                {
                    _logger.LogInformation("✅ Error status reset successfully on {DeviceName}", deviceName);
                }
                else
                {
                    _logger.LogError("❌ Error status reset failed on {DeviceName} - Status: {Status}, Payload: {Payload}",
                        deviceName, result.Status, result.GetPayloadAsJson());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting error status for device {DeviceId}", deviceId);
                throw; // Re-throw to let Function runtime handle it
            }
        }

        private string GetDeviceName(int deviceId)
        {
            // Replace {id} placeholder with actual device ID
            if (_deviceIdTemplate.Contains("{id}"))
            {
                return _deviceIdTemplate.Replace("{id}", deviceId.ToString());
            }

            // If no placeholder, append device ID (fallback)
            return $"{_deviceIdTemplate}{deviceId}";
        }

        private int GetCurrentProductionRate(Twin twin)
        {
            // Default rate if not found
            int currentRate = 100;

            // Try different property names that might be used by the agent
            if (twin.Properties.Reported.Contains("productionRate"))
            {
                currentRate = twin.Properties.Reported["productionRate"];
            }
            else if (twin.Properties.Reported.Contains($"device{twin.DeviceId}_productionRate"))
            {
                // Agent might use device-specific property names
                currentRate = twin.Properties.Reported[$"device{twin.DeviceId}_productionRate"];
            }
            else if (twin.Properties.Desired.Contains("productionRate"))
            {
                // Fall back to desired properties if reported not available
                currentRate = twin.Properties.Desired["productionRate"];
            }

            _logger.LogDebug("Retrieved production rate: {Rate}% for device {DeviceId}", currentRate, twin.DeviceId);
            return currentRate;
        }

        public void Dispose()
        {
            _logger.LogInformation("Disposing AzureIoTService");
            _serviceClient?.Dispose();
            _registryManager?.Dispose();
        }
    }
}
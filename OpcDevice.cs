using Opc.UaFx;
using Opc.UaFx.Client;
using System;
using System.Threading.Tasks;

namespace IndustrialIoT.Agent
{
    public class OpcDevice
    {
        private readonly string _serverUrl;
        private readonly int _deviceId;
        private OpcClient? _client;

        public OpcDevice(string serverUrl, int deviceId)
        {
            _serverUrl = serverUrl;
            _deviceId = deviceId;
        }

        public async Task ConnectAsync()
        {
            _client = new OpcClient(_serverUrl);
            _client.Connect();
            Console.WriteLine($"Connected to OPC UA Server: {_serverUrl}");

            // Verify device exists
            await VerifyDeviceExistsAsync();
        }

        public async Task DisconnectAsync()
        {
            _client?.Disconnect();
            _client?.Dispose();
            Console.WriteLine("Disconnected from OPC UA Server");
            await Task.CompletedTask;
        }

        private async Task VerifyDeviceExistsAsync()
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                // Try to read production status to verify device exists
                var productionStatus = _client.ReadNode($"ns=2;s=Device {_deviceId}/ProductionStatus");
                Console.WriteLine($"Device {_deviceId} found - Initial status: {productionStatus.Value}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Device {_deviceId} not found on OPC UA server. Error: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public async Task<TelemetryData?> ReadTelemetryDataAsync()
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                var productionStatus = (int)_client.ReadNode($"ns=2;s=Device {_deviceId}/ProductionStatus").Value;
                var workorderId = (string)_client.ReadNode($"ns=2;s=Device {_deviceId}/WorkorderId").Value;
                var goodCount = (long)_client.ReadNode($"ns=2;s=Device {_deviceId}/GoodCount").Value;
                var badCount = (long)_client.ReadNode($"ns=2;s=Device {_deviceId}/BadCount").Value;
                var temperature = (double)_client.ReadNode($"ns=2;s=Device {_deviceId}/Temperature").Value;

                return await Task.FromResult(new TelemetryData
                {
                    DeviceId = _deviceId,
                    ProductionStatus = (ProductionStatus)productionStatus,
                    WorkorderId = string.IsNullOrEmpty(workorderId) ? Guid.Empty : Guid.Parse(workorderId),
                    GoodCount = goodCount,
                    BadCount = badCount,
                    Temperature = temperature,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading telemetry data for device {_deviceId}: {ex.Message}");
                return null;
            }
        }

        public async Task<DeviceState?> ReadDeviceStateAsync()
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                var productionRate = (int)_client.ReadNode($"ns=2;s=Device {_deviceId}/ProductionRate").Value;
                var deviceErrors = (int)_client.ReadNode($"ns=2;s=Device {_deviceId}/DeviceError").Value;

                return await Task.FromResult(new DeviceState
                {
                    DeviceId = _deviceId,
                    ProductionRate = productionRate,
                    DeviceErrors = (DeviceErrors)deviceErrors,
                    LastUpdated = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading device state for device {_deviceId}: {ex.Message}");
                return null;
            }
        }

        public async Task EmergencyStopAsync()
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                _client.CallMethod($"ns=2;s=Device {_deviceId}", $"ns=2;s=Device {_deviceId}/EmergencyStop");
                Console.WriteLine($"Emergency stop executed for device {_deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing emergency stop for device {_deviceId}: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public async Task ResetErrorStatusAsync()
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                _client.CallMethod($"ns=2;s=Device {_deviceId}", $"ns=2;s=Device {_deviceId}/ResetErrorStatus");
                Console.WriteLine($"Error status reset for device {_deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting error status for device {_deviceId}: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public async Task SetProductionRateAsync(int productionRate)
        {
            try
            {
                if (_client == null)
                    throw new InvalidOperationException("OPC client not connected");

                _client.WriteNode($"ns=2;s=Device {_deviceId}/ProductionRate", productionRate);
                Console.WriteLine($"Production rate set to {productionRate}% for device {_deviceId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting production rate for device {_deviceId}: {ex.Message}");
            }
            await Task.CompletedTask;
        }
    }

    // Data models
    public class TelemetryData
    {
        public int DeviceId { get; set; }
        public ProductionStatus ProductionStatus { get; set; }
        public Guid WorkorderId { get; set; }
        public long GoodCount { get; set; }
        public long BadCount { get; set; }
        public double Temperature { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class DeviceState
    {
        public int DeviceId { get; set; }
        public int ProductionRate { get; set; }
        public DeviceErrors DeviceErrors { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public enum ProductionStatus
    {
        Stopped = 0,
        Running = 1
    }

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
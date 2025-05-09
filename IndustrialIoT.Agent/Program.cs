using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialIoT.Agent
{
    class Program
    {
        private static IConfiguration? _configuration;
        private static string? _deviceConnectionString;
        private static string? _opcServerUrl;
        private static int _deviceId;
        private static int _telemetryInterval;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Industrial IoT Agent Starting...");

            try
            {
                // Load configuration
                LoadConfiguration();

                // Initialize Azure IoT Device Client
                using var deviceClient = DeviceClient.CreateFromConnectionString(
                    _deviceConnectionString,
                    TransportType.Mqtt);
                await deviceClient.OpenAsync();

                // Initialize IoT Device
                var iotDevice = new IoTDevice(deviceClient);
                await iotDevice.InitializeHandlersAsync();

                // Initialize OPC Device
                var opcDevice = new OpcDevice(_opcServerUrl!, _deviceId);
                await opcDevice.ConnectAsync();

                // Connect OPC device to IoT device
                iotDevice.SetOpcDevice(opcDevice);

                Console.WriteLine($"Agent connected to device {_deviceId} on OPC UA Server: {_opcServerUrl}");
                Console.WriteLine($"Azure IoT Hub connection established");
                Console.WriteLine("Press 'q' to quit...");

                // Start main monitoring loop
                var cts = new CancellationTokenSource();
                var monitoringTask = MonitorDeviceAsync(iotDevice, opcDevice, cts.Token);

                // Wait for user input to quit
                while (Console.ReadKey(true).KeyChar != 'q')
                {
                    // Keep running
                }

                cts.Cancel();
                await monitoringTask;

                // Cleanup
                await opcDevice.DisconnectAsync();
                await deviceClient.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("Agent stopped.");
        }

        private static void LoadConfiguration()
        {
            // Build configuration
            _configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Load connection strings
            _deviceConnectionString = _configuration.GetConnectionString("AzureIoTHub");
            _opcServerUrl = _configuration.GetConnectionString("OpcServer");

            // Load device settings
            _deviceId = _configuration.GetValue<int>("DeviceSettings:DeviceId");
            _telemetryInterval = _configuration.GetValue<int>("DeviceSettings:TelemetryIntervalMs");

            // Validate configuration
            if (string.IsNullOrEmpty(_deviceConnectionString))
                throw new ArgumentException("Azure IoT Hub connection string is missing in appsettings.json");

            if (string.IsNullOrEmpty(_opcServerUrl))
                throw new ArgumentException("OPC Server URL is missing in appsettings.json");

            if (_deviceId <= 0)
                throw new ArgumentException("DeviceId must be greater than 0 in appsettings.json");

            if (_telemetryInterval <= 0)
                _telemetryInterval = 1000; // Default to 1 second

            Console.WriteLine($"Configuration loaded:");
            Console.WriteLine($"  Device ID: {_deviceId}");
            Console.WriteLine($"  OPC Server: {_opcServerUrl}");
            Console.WriteLine($"  Telemetry Interval: {_telemetryInterval}ms");
        }

        private static async Task MonitorDeviceAsync(IoTDevice iotDevice, OpcDevice opcDevice, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Starting monitoring loop for device {_deviceId}...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read telemetry data from OPC device
                    var telemetryData = await opcDevice.ReadTelemetryDataAsync();
                    if (telemetryData != null)
                    {
                        // Send telemetry to Azure IoT Hub
                        await iotDevice.SendTelemetryAsync(telemetryData);

                        // Debug logging if enabled
                        if (_configuration?.GetValue<bool>("DeviceSettings:EnableDebugLogging") == true)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Telemetry sent - Status: {telemetryData.ProductionStatus}, Temp: {telemetryData.Temperature:F1}°C");
                        }
                    }

                    // Read and update device twin if needed
                    var deviceState = await opcDevice.ReadDeviceStateAsync();
                    if (deviceState != null)
                    {
                        await iotDevice.UpdateDeviceTwinAsync(deviceState);

                        // Check for device errors and send event if any
                        if (deviceState.DeviceErrors != DeviceErrors.None)
                        {
                            await iotDevice.SendDeviceErrorEventAsync(deviceState.DeviceErrors);
                        }
                    }

                    await Task.Delay(_telemetryInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in monitoring loop: {ex.Message}");
                    await Task.Delay(5000, cancellationToken); // Wait 5 seconds before retrying
                }
            }

            Console.WriteLine("Monitoring loop stopped.");
        }
    }
}
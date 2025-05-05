using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace IndustrialIoT.Agent
{
    public class IoTDevice
    {
        private readonly DeviceClient _deviceClient;
        private OpcDevice? _opcDevice;
        private DeviceState? _lastKnownState;

        public IoTDevice(DeviceClient deviceClient)
        {
            _deviceClient = deviceClient;
        }

        public void SetOpcDevice(OpcDevice opcDevice)
        {
            _opcDevice = opcDevice;
        }

        public async Task InitializeHandlersAsync()
        {
            await _deviceClient.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, _deviceClient);
            await _deviceClient.SetMethodHandlerAsync("EmergencyStop", EmergencyStopHandler, _deviceClient);
            await _deviceClient.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatusHandler, _deviceClient);
            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, _deviceClient);
            await _deviceClient.SetMethodDefaultHandlerAsync(DefaultMethodHandler, _deviceClient);

            Console.WriteLine("IoT Device handlers initialized");
        }

        public async Task SendTelemetryAsync(TelemetryData telemetryData)
        {
            if (telemetryData == null) return;

            try
            {
                var telemetryMessage = new
                {
                    deviceId = telemetryData.DeviceId,
                    productionStatus = telemetryData.ProductionStatus.ToString(),
                    workorderId = telemetryData.WorkorderId == Guid.Empty ? null : telemetryData.WorkorderId.ToString(),
                    goodCount = telemetryData.GoodCount,
                    badCount = telemetryData.BadCount,
                    temperature = telemetryData.Temperature,
                    timestamp = telemetryData.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var messageString = JsonConvert.SerializeObject(telemetryMessage);
                var message = new Message(Encoding.UTF8.GetBytes(messageString))
                {
                    ContentType = MediaTypeNames.Application.Json,
                    ContentEncoding = "utf-8"
                };

                message.Properties.Add("messageType", "telemetry");
                message.Properties.Add("deviceId", telemetryData.DeviceId.ToString());

                await _deviceClient.SendEventAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending telemetry: {ex.Message}");
            }
        }

        public async Task SendDeviceErrorEventAsync(DeviceErrors deviceErrors)
        {
            try
            {
                var errorEvent = new
                {
                    deviceId = _lastKnownState?.DeviceId ?? 0,
                    errorType = "deviceError",
                    errors = deviceErrors.ToString(),
                    errorCode = (int)deviceErrors,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };

                var messageString = JsonConvert.SerializeObject(errorEvent);
                var message = new Message(Encoding.UTF8.GetBytes(messageString))
                {
                    ContentType = MediaTypeNames.Application.Json,
                    ContentEncoding = "utf-8"
                };

                message.Properties.Add("messageType", "error");
                message.Properties.Add("deviceId", (_lastKnownState?.DeviceId ?? 0).ToString());

                await _deviceClient.SendEventAsync(message);

                Console.WriteLine($"Device error event sent: {deviceErrors}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending device error event: {ex.Message}");
            }
        }

        public async Task UpdateDeviceTwinAsync(DeviceState deviceState)
        {
            if (deviceState == null) return;

            try
            {
                // Check if state has changed
                var hasChanged = false;
                if (_lastKnownState != null)
                {
                    hasChanged = _lastKnownState.ProductionRate != deviceState.ProductionRate ||
                                _lastKnownState.DeviceErrors != deviceState.DeviceErrors;
                }
                else
                {
                    hasChanged = true; // First time
                }

                if (hasChanged)
                {
                    var reportedProperties = new TwinCollection();
                    reportedProperties["productionRate"] = deviceState.ProductionRate;
                    reportedProperties["deviceErrors"] = deviceState.DeviceErrors.ToString();
                    reportedProperties["lastUpdated"] = deviceState.LastUpdated.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                    await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);

                    // Update last known state
                    _lastKnownState = deviceState;

                    Console.WriteLine($"Device twin updated - Rate: {deviceState.ProductionRate}%, Errors: {deviceState.DeviceErrors}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating device twin: {ex.Message}");
            }
        }

        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object userContext)
        {
            Console.WriteLine($"C2D message received: {Encoding.UTF8.GetString(receivedMessage.GetBytes())}");
            await _deviceClient.CompleteAsync(receivedMessage);
        }

        private async Task<MethodResponse> EmergencyStopHandler(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Console.WriteLine("Emergency Stop method called");

                if (_opcDevice != null)
                {
                    await _opcDevice.EmergencyStopAsync();
                }

                var response = new { success = true, message = "Emergency stop executed" };
                return new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), 200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Emergency Stop handler: {ex.Message}");
                var response = new { success = false, message = ex.Message };
                return new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), 500);
            }
        }

        private async Task<MethodResponse> ResetErrorStatusHandler(MethodRequest methodRequest, object userContext)
        {
            try
            {
                Console.WriteLine("Reset Error Status method called");

                if (_opcDevice != null)
                {
                    await _opcDevice.ResetErrorStatusAsync();
                }

                var response = new { success = true, message = "Error status reset" };
                return new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), 200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Reset Error Status handler: {ex.Message}");
                var response = new { success = false, message = ex.Message };
                return new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), 500);
            }
        }

        private async Task<MethodResponse> DefaultMethodHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"Unknown method called: {methodRequest.Name}");
            await Task.CompletedTask;
            var response = new { success = false, message = $"Unknown method: {methodRequest.Name}" };
            return new MethodResponse(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response)), 404);
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            try
            {
                Console.WriteLine($"Desired property update received");

                // Check for production rate change
                if (desiredProperties.Contains("productionRate"))
                {
                    var newProductionRate = Convert.ToInt32(desiredProperties["productionRate"]);

                    if (_opcDevice != null)
                    {
                        await _opcDevice.SetProductionRateAsync(newProductionRate);
                    }

                    // Update reported properties to confirm the change
                    var reportedProperties = new TwinCollection();
                    reportedProperties["productionRate"] = newProductionRate;
                    await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties);

                    Console.WriteLine($"Production rate set to {newProductionRate}%");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling desired property change: {ex.Message}");
            }
        }
    }
}
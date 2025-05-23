# Industrial IoT Monitoring Solution

## Overview
This project implements an industrial IoT monitoring solution that collects telemetry from OPC UA devices, processes the data through Azure IoT Hub, and analyzes it using Stream Analytics. The system provides real-time production KPIs, temperature monitoring, and error detection for manufacturing environments.

## Architecture

```
┌─────────────┐     ┌─────────────┐     ┌──────────────────┐     ┌───────────────────┐
│ OPC UA      │     │ IoT Agent   │     │ Azure IoT Hub    │     │ Stream Analytics  │
│ Devices     │────>│ (.NET Core) │────>│ (Cloud Gateway)  │────>│ (Data Processing) │
└─────────────┘     └─────────────┘     └──────────────────┘     └───────────────────┘
                                                                          │
                                                                          ▼
                                                              ┌───────────────────────┐
                                                              │ Azure Functions       │
                                                              │ (Business Logic)      │
                                                              └───────────────────────┘
```

## Key Components

### IndustrialIoT.Agent
- Connects to industrial devices via OPC UA
- Collects telemetry data (temperature, production counts, errors)
- Sends data securely to Azure IoT Hub

### IndustrialIoT.Functions
- Processes messages from IoT Hub
- Implements business logic for data analysis
- Provides APIs for accessing aggregated information

### Stream Analytics
- Real-time data processing
- Production KPI calculation
- Temperature statistics monitoring
- Error detection and alerting

## Setup Instructions

### Prerequisites
- .NET 6.0 SDK or later
- Azure subscription
- Azure IoT Hub instance
- Stream Analytics job
- OPC UA compliant devices or simulators

### Local Development
1. Clone this repository
2. Update `AppSettings.json` with your device connection information
3. Update `local.settings.json` with your Azure connection strings
4. Run the agent project:
   ```
   cd IndustrialIoT.Agent
   dotnet run
   ```
5. Run the functions project locally:
   ```
   cd IndustrialIoT.Functions
   func start
   ```

### Deployment
1. Deploy the Functions app to Azure:
   ```
   cd IndustrialIoT.Functions
   func azure functionapp publish <FunctionAppName>
   ```
2. Create and start a Stream Analytics job using the queries in `stream_analitics_kwerendy.txt`
3. Deploy the IoT Agent to your edge devices

## Stream Analytics Queries

The solution includes three main analytics queries:
- Production KPIs - Calculates quality metrics based on good/bad product counts
- Temperature Statistics - Monitors min/max/avg temperature values
- Device Error Detection - Identifies devices reporting frequent errors

## License
MIT

## Contact
For questions and support, please open an issue in this repository.
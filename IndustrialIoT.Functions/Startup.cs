using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(IndustrialIoT.Functions.Startup))]

namespace IndustrialIoT.Functions
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // Register Azure IoT Service
            builder.Services.AddSingleton<IAzureIoTService, AzureIoTService>();
        }
    }
}
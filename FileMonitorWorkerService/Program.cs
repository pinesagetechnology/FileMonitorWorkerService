using FileMonitorWorkerService;
using FileMonitorWorkerService.Data;
using FileMonitorWorkerService.Services;
using Microsoft.Extensions.Hosting.WindowsServices; // Add this using directive for Windows services support

var builder = Host.CreateDefaultBuilder(args);

if (OperatingSystem.IsWindows())
{
    builder.UseWindowsService(); // This requires the Microsoft.Extensions.Hosting.WindowsServices package
}
else if (OperatingSystem.IsLinux())
{
    builder.UseSystemd();
}

// Fix: Use the correct method to configure logging
builder.ConfigureLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
});

builder.ConfigureServices((hostContext, services) =>
{
    DataLayerExtension.RegisterDataLayer(services, hostContext.Configuration);
    ServiceLayerExtension.RegisterServiceLayer(services, hostContext.Configuration);

    services.AddHostedService<Worker>();
});

var host = builder.Build();

// Seed configuration defaults on startup
using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    var logger = services.GetRequiredService<ILogger<DatabaseInitializer>>();
    await DatabaseInitializer.InitializeAsync(context, logger);
}

host.Run();

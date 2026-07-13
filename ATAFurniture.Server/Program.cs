using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ATAFurniture.Server;

public class Program
{
    private static readonly string Environment
        = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    
    public static IConfiguration Configuration { get; private set; } = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{Environment}.json", optional: true)
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>()
        .Build();
    
    public static void Main(string[] args)
    {
        var loggerConfig = new LoggerConfiguration().ReadFrom.Configuration(Configuration);
        Log.Logger = loggerConfig.CreateLogger();
        
        try
        {
            Log.Information("Starting...");
            var host = CreateHostBuilder(args).Build();

            var key = Configuration.GetSection("SyncfusionLicenseKey").Value;
            if (!string.IsNullOrEmpty(key))
            {
                Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(key);
            }
            else if (string.Equals(Environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                // Development-only: allow running without a Syncfusion licence (e.g. a lost/rotated
                // key). Components may render a licensing dialog. Production still requires the key.
                Log.Warning("SyncfusionLicenseKey is not set; continuing without a Syncfusion licence (Development only).");
            }
            else
            {
                throw new Exception("SyncfusionLicenseKey is not set");
            }
            
            
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog((_, c) =>
            {
                c.Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File(
                        path: "logs/log_.txt", 
                        rollingInterval: RollingInterval.Day, 
                        retainedFileCountLimit: 7, 
                        rollOnFileSizeLimit: true, 
                        fileSizeLimitBytes: 10485760)
                    .WriteTo.Sentry();
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseConfiguration(Configuration)
                    .UseSentry(options =>
                    {
                        options.Dsn = Configuration.GetSection("Sentry:Dsn").Value;
                        options.MinimumEventLevel = Environment == "Production" ? LogLevel.Warning : LogLevel.Debug;
                    });
                webBuilder.UseStartup<Startup>();
            });
}
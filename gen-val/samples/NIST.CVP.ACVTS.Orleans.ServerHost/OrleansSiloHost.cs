using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NIST.CVP.ACVTS.Libraries.Common.Config;
using NIST.CVP.ACVTS.Libraries.Orleans.Grains;
using NIST.CVP.ACVTS.Libraries.Orleans.Grains.Interfaces;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Statistics;
using Serilog.Extensions.Logging;

namespace NIST.CVP.ACVTS.Orleans.ServerHost
{
    public class OrleansSiloHost : IHostedService
    {
        private readonly ILogger<OrleansSiloHost> _logger;
        private readonly IConfiguration _configuration;
        private readonly OrleansConfig _orleansConfig;

        private ISiloHost _silo;

        public OrleansSiloHost(
            ILogger<OrleansSiloHost> logger,
            IConfiguration configuration,
            IOptions<OrleansConfig> orleansConfig)
        {
            _logger = logger;
            _logger.LogInformation("Orleans Silo initializing.");

            _configuration = configuration;
            _orleansConfig = orleansConfig.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Orleans Silo starting...");
            var builder = new SiloHostBuilder()
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = _orleansConfig.ClusterId;
                    options.ServiceId = Constants.ServiceId;
                })
                .Configure<GrainCollectionOptions>(options => { options.CollectionAge = TimeSpan.FromMinutes(5); })
                .ConfigureApplicationParts(parts =>
                {
                    parts.AddApplicationPart(typeof(IGrainMarker).Assembly).WithReferences();
                })
                .ConfigureServices(svcCollection =>
                {
                    ConfigureServices.RegisterServices(_configuration, svcCollection);
                })
                .UsePerfCounterEnvironmentStatistics()
                .UseDashboard(options =>
                {
                    options.Port = _orleansConfig.OrleansDashboardPort;
                    options.CounterUpdateIntervalMs = 10000;
                });

            ConfigureClustering(builder);
            ConfigureLoadShedding(builder);
            ConfigureLogging(builder);

            _silo = builder.Build();
            await _silo.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _silo.StopAsync(cancellationToken);
        }

        private void ConfigureClustering(ISiloHostBuilder builder)
{
    var clusteringType = _configuration["OrleansConfig:ClusteringType"];
    
    if (string.Equals(clusteringType, "AdoNet", StringComparison.OrdinalIgnoreCase))
    {
        // ADO.NET Clustering (MySQL, SQL Server, etc.)
        var invariant = _configuration["OrleansConfig:ClusteringInvariant"];
        var connectionString = _configuration["OrleansConfig:ClusteringConnectionString"];
        var advertisedIP = _configuration["OrleansConfig:AdvertisedIP"];
        
        builder.Configure<EndpointOptions>(options =>
        {
            options.SiloPort = _orleansConfig.OrleansSiloPort;
            options.GatewayPort = _orleansConfig.OrleansGatewayPort;
            
            if (!string.IsNullOrEmpty(advertisedIP))
            {
                options.AdvertisedIPAddress = IPAddress.Parse(advertisedIP);
            }
            else
            {
                // Auto-detect IP
                options.AdvertisedIPAddress = GetLocalIPAddress();
            }
        });
        
        builder.UseAdoNetClustering(options =>
        {
            options.Invariant = invariant;
            options.ConnectionString = connectionString;
        });
        
        _logger.LogInformation($"Using AdoNet clustering with {invariant}");
    }
    else
    {
        // Default: Localhost clustering (original behavior)
        builder.Configure<EndpointOptions>(options =>
        {
            options.AdvertisedIPAddress = IPAddress.Loopback;
        });
        builder.UseLocalhostClustering();
        
        _logger.LogInformation("Using localhost clustering");
    }
}

private static IPAddress GetLocalIPAddress()
{
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(ip))
        {
            return ip;
        }
    }
    return IPAddress.Loopback;
}


        private void ConfigureLoadShedding(ISiloHostBuilder builder)
        {
            if (_orleansConfig.LoadSheddingCpuThreshold > 0)
            {
                builder.Configure<LoadSheddingOptions>(options =>
                {
                    options.LoadSheddingEnabled = true;
                    options.LoadSheddingLimit = _orleansConfig.LoadSheddingCpuThreshold;
                });
            }
        }

        private void ConfigureLogging(ISiloHostBuilder builder)
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(_orleansConfig.MinimumLogLevel);
                logging.AddConsole();
                logging.AddProvider(new SerilogLoggerProvider());
            });
        }
    }
}

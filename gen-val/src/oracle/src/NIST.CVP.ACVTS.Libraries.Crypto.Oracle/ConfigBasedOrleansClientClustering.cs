using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NIST.CVP.ACVTS.Libraries.Common.Config;
using NIST.CVP.ACVTS.Libraries.Common.Enums;
using NIST.CVP.ACVTS.Libraries.Common.Interfaces;
using NIST.CVP.ACVTS.Libraries.Orleans.Grains.Interfaces;
using Orleans;
using Orleans.Hosting;

namespace NIST.CVP.ACVTS.Libraries.Crypto.Oracle
{
    public class ConfigBasedOrleansClientClustering : IOrleansClientClustering
    {
        private readonly string _orleansConnectionString;
        private readonly IConfiguration _configuration;
        private readonly IOptions<EnvironmentConfig> _environmentConfig;
        private readonly IOptions<OrleansConfig> _orleansConfig;

        public ConfigBasedOrleansClientClustering(
            IDbConnectionStringFactory dbConnectionStringFactory, 
            IConfiguration configuration,
            IOptions<EnvironmentConfig> environmentConfig, 
            IOptions<OrleansConfig> orleansConfig)
        {
            _orleansConnectionString = dbConnectionStringFactory
                .GetConnectionString(Constants.OrleansConnectionString);
            _configuration = configuration;
            _environmentConfig = environmentConfig;
            _orleansConfig = orleansConfig;
        }

        public void ConfigureClustering(IClientBuilder builder)
        {
            // First, check if explicit ClusteringType is configured (matches silo configuration)
            var clusteringType = _configuration["OrleansConfig:ClusteringType"];
            
            if (!string.IsNullOrEmpty(clusteringType))
            {
                ConfigureByClusteringType(builder, clusteringType);
                return;
            }
            
            // Fall back to environment-based configuration (original behavior)
            ConfigureByEnvironment(builder);
        }
        
        private void ConfigureByClusteringType(IClientBuilder builder, string clusteringType)
        {
            if (string.Equals(clusteringType, "AdoNet", System.StringComparison.OrdinalIgnoreCase))
            {
                // ADO.NET Clustering (MySQL, SQL Server, etc.) - matches silo configuration
                var invariant = _configuration["OrleansConfig:ClusteringInvariant"];
                var connectionString = _configuration["OrleansConfig:ClusteringConnectionString"];
                
                builder.UseAdoNetClustering(options =>
                {
                    options.Invariant = invariant;
                    options.ConnectionString = connectionString;
                });
            }
            else if (string.Equals(clusteringType, "Static", System.StringComparison.OrdinalIgnoreCase))
            {
                // Static clustering using configured endpoints
                List<IPEndPoint> endpoints = new List<IPEndPoint>();
                foreach (var endpoint in _orleansConfig.Value.OrleansNodeConfig.Select(s => s.HostName))
                {
                    endpoints.Add(new IPEndPoint(
                        IPAddress.Parse(endpoint), _orleansConfig.Value.OrleansGatewayPort
                    ));
                }
                builder.UseStaticClustering(endpoints.ToArray());
            }
            else
            {
                // Default: Localhost clustering
                builder.UseLocalhostClustering();
            }
        }
        
        private void ConfigureByEnvironment(IClientBuilder builder)
        {
            // Original environment-based logic
            switch (_environmentConfig.Value.Name)
            {
                case Environments.Local:
                    builder.UseLocalhostClustering();
                    break;
                case Environments.Tc:
                    List<IPEndPoint> endpoints = new List<IPEndPoint>();
                    foreach (var endpoint in _orleansConfig.Value.OrleansNodeConfig.Select(s => s.HostName))
                    {
                        endpoints.Add(new IPEndPoint(
                            IPAddress.Parse(endpoint), _orleansConfig.Value.OrleansGatewayPort
                        ));
                    }
                    builder.UseStaticClustering(endpoints.ToArray());
                    break;
                default:
                    builder.UseAdoNetClustering(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.ConnectionString = _orleansConnectionString;
                    });
                    break;
            }
        }
    }
}
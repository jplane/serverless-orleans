using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace Frontend
{
    public class ActorClientService : IHostedService
    {
        private readonly ILogger<ActorClientService> _log;
        private readonly IConfiguration _config;

        public IClusterClient Client { get; }

        public ActorClientService(IConfiguration config, ILogger<ActorClientService> log)
        {
            _config = config;
            _log = log;

            var builder =
                new ClientBuilder()
                    .Configure<ClusterOptions>(options =>
                    {
                        options.ClusterId = "serverlessorleans";
                        options.ServiceId = "serverlessorleans";
                    })
                    .ConfigureLogging(builder => builder.AddConsole())
                    .ConfigureApplicationParts(parts => parts.AddFromApplicationBaseDirectory())
                    .UseDashboard();

            var env = _config["ORLEANS_CONFIG"];

            if (env == "SQL")
            {
                builder =
                    builder.UseAdoNetClustering(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                    });
            }
            else if (env == "STORAGE")
            {
                builder =
                    builder.UseAzureStorageClustering(options =>
                    {
                        options.TableName = "clusterstate";
                        options.ConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                    });
            }
            else
            {
                throw new Exception("ORLEANS_CONFIG envvar not defined.");
            }

            Client = builder.Build();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var attempt = 0;
            var maxAttempts = 10;
            var delay = TimeSpan.FromSeconds(6);

            return Client.Connect(async error =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (++attempt < maxAttempts)
                {
                    _log.LogWarning(error,
                        "Failed to connect to Orleans cluster on attempt {@Attempt} of {@MaxAttempts}.",
                        attempt, maxAttempts);

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }

                    return true;
                }
                else
                {
                    _log.LogError(error,
                        "Failed to connect to Orleans cluster on attempt {@Attempt} of {@MaxAttempts}.",
                        attempt, maxAttempts);

                    return false;
                }
            });
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Client.Close();
            Client.Dispose();
        }
    }
}

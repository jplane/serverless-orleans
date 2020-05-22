using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Runtime.Loader;

namespace Backend
{
    public class Program
    {
        private static ISiloHost _silo;
        private static MetricsWriter _metricsWriter;
        private static readonly ManualResetEvent _siloStopped = new ManualResetEvent(false);
        
        public static void Main(string[] args)
        {
            var env = Environment.GetEnvironmentVariable("ORLEANS_CONFIG");

            _metricsWriter = new MetricsWriter();

            var builder = new SiloHostBuilder();

            builder
                .ConfigureLogging((context, loggingBuilder) => loggingBuilder.AddConsole())
                .Configure<ProcessExitHandlingOptions>(options =>
                {
                    options.FastKillOnProcessExit = false;
                })
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "serverlessorleans";
                    options.ServiceId = "serverlessorleans";
                });

            if (env == "STORAGE")
            {
                builder
                    .AddAzureBlobGrainStorageAsDefault(options =>
                    {
                        options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                        options.UseJson = true;
                        options.ContainerName = "actorstate";
                    })
                    .UseAzureStorageClustering(options =>
                    {
                        options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                        options.TableName = "clusterstate";
                    })
                    .ConfigureEndpoints(11111, 30000);
            }
            else if (env == "SQL")
            {
                builder
                    .AddAdoNetGrainStorageAsDefault(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.UseJsonFormat = true;
                        options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                    })
                    .UseAdoNetClustering(options =>
                    {
                        options.Invariant = "System.Data.SqlClient";
                        options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                    })
                    .ConfigureEndpoints(11111, 30000);
            }
            else
            {
                throw new Exception("ORLEANS_CONFIG envvar not defined.");
            }

            _silo = builder.Build();

            Task.Run(StartSilo);

            AssemblyLoadContext.Default.Unloading += context =>
            {
                Task.Run(StopSilo);
                _siloStopped.WaitOne();
            };

            _siloStopped.WaitOne();
        }

        private static async Task StartSilo()
        {
            await _silo.StartAsync();
            await _metricsWriter.StartAsync();
            Console.WriteLine("Silo started");
        }

        private static async Task StopSilo()
        {
            await _metricsWriter.StopAsync();
            await _silo.StopAsync();
            Console.WriteLine("Silo stopped");
            _siloStopped.Set();
        }
    }
}

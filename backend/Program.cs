using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace Backend
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureLogging((context, loggingBuilder) => loggingBuilder.AddConsole());

            var env = Environment.GetEnvironmentVariable("ORLEANS_CONFIG");

            builder.UseOrleans((ctxt, siloBuilder) =>
            {
                siloBuilder = siloBuilder.Configure<ProcessExitHandlingOptions>(options =>
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
                    siloBuilder
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
                    siloBuilder
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
            });

            return builder.RunConsoleAsync();
        }
    }
}

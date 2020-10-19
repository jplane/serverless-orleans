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
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Diagnostics;

namespace Backend
{
    public class Program
    {
        private static ISiloHost _silo;
        private static readonly ManualResetEvent _siloStopped = new ManualResetEvent(false);
        
        public static void Main(string[] args)
        {
            var env = Environment.GetEnvironmentVariable("ORLEANS_CONFIG");

            ISiloHostBuilder builder = new SiloHostBuilder();

            ConfigureOrleansBase(builder);

            if (env == "STORAGE")
            {
                ConfigureOrleansStorage(builder);
            }
            else if (env == "SQL")
            {
                ConfigureOrleansSQL(builder);
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

        private static void ConfigureOrleansSQL(ISiloHostBuilder builder)
        {
            builder
                .AddAdoNetGrainStorageAsDefault(options =>
                {
                    options.Invariant = "System.Data.SqlClient";
                    options.UseJsonFormat = true;
                    options.IndentJson = true;
                    options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                })
                .UseAdoNetClustering(options =>
                {
                    options.Invariant = "System.Data.SqlClient";
                    options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                });
        }

        private static void ConfigureOrleansStorage(ISiloHostBuilder builder)
        {
            builder
                .AddAzureBlobGrainStorageAsDefault(options =>
                {
                    options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                    options.UseJson = true;
                    options.IndentJson = true;
                    options.ContainerName = "actorstate";
                })
                .UseAzureStorageClustering(options =>
                {
                    options.ConnectionString = Environment.GetEnvironmentVariable("StorageConnectionString");
                    options.TableName = "clusterstate";
                });
        }

        private static void ConfigureOrleansBase(ISiloHostBuilder builder)
        {
            var externalAssemblies = LoadExternalAssemblies().ToArray();

            Debug.Assert(externalAssemblies != null);

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
                })
                .ConfigureApplicationParts(parts =>
                {
                    foreach(var assembly in externalAssemblies)
                    {
                        Trace.WriteLine("Loading orleans app parts: " + assembly.FullName);
                        parts.AddApplicationPart(assembly);
                    }
                })
                .UseDashboard(options =>
                {
                    options.CounterUpdateIntervalMs = 5000;
                    options.HostSelf = false;
                })
                .ConfigureEndpoints(11111, 30000);
        }

        private static async Task StartSilo()
        {
            await _silo.StartAsync();
            Trace.WriteLine("Silo started");
        }

        private static async Task StopSilo()
        {
            await _silo.StopAsync();
            Trace.WriteLine("Silo stopped");
            _siloStopped.Set();
        }

        private static IEnumerable<Assembly> LoadExternalAssemblies()
        {
            var appPath = AppDomain.CurrentDomain.BaseDirectory + "/app";

            Debug.Assert(!string.IsNullOrWhiteSpace(appPath));

            foreach(var assemblyPath in Directory.GetFiles(appPath, "*.dll"))
            {
                yield return Assembly.LoadFrom(assemblyPath);
            }
        }
    }
}

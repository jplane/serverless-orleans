using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Diagnostics;

namespace Frontend
{
    public class ActorClientService : IHostedService
    {
        private static List<Assembly> _externalAssemblies = new List<Assembly>();

        private readonly ILogger<ActorClientService> _log;
        private readonly IConfiguration _config;

        public IClusterClient Client { get; }

        public ActorClientService(IConfiguration config, ILogger<ActorClientService> log)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            _config = config;
            _log = log;

            IClientBuilder builder = new ClientBuilder();

            ConfigureOrleansBase(builder);

            var env = _config["ORLEANS_CONFIG"];

            if (env == "SQL")
            {
                ConfigureOrleansSQL(builder);
            }
            else if (env == "STORAGE")
            {
                ConfigureOrleansStorage(builder);
            }
            else
            {
                throw new Exception("ORLEANS_CONFIG envvar not defined.");
            }

            Client = builder.Build();
        }

        private void ConfigureOrleansStorage(IClientBuilder builder)
        {
            builder
                .UseAzureStorageClustering(options =>
                {
                    options.TableName = "clusterstate";
                    options.ConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                });
        }

        private void ConfigureOrleansSQL(IClientBuilder builder)
        {
            builder
                .UseAdoNetClustering(options =>
                {
                    options.Invariant = "System.Data.SqlClient";
                    options.ConnectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                });
        }

        private void ConfigureOrleansBase(IClientBuilder builder)
        {
            builder
                .Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "serverlessorleans";
                    options.ServiceId = "serverlessorleans";
                })
                .ConfigureLogging(builder => builder.AddConsole())
                .ConfigureApplicationParts(parts =>
                {
                    foreach(var assembly in ExternalAssemblies)
                    {
                        System.Console.WriteLine("Loading orleans app parts: " + assembly.FullName);
                        parts.AddApplicationPart(assembly);
                    }
                })
                .UseDashboard();
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
                    _log.LogWarning(
                        error,
                        $"Failed to connect to Orleans cluster on attempt {attempt} of {maxAttempts}.");

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
                        $"Failed to connect to Orleans cluster on attempt {attempt} of {maxAttempts}.");

                    return false;
                }
            });
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Client.Close();
            Client.Dispose();
        }

        public static IEnumerable<Assembly> ExternalAssemblies
        {
            get
            {
                lock(_externalAssemblies)
                {
                    if (_externalAssemblies.Count == 0)
                    {
                        var appPath = AppDomain.CurrentDomain.BaseDirectory + "/app";

                        Debug.Assert(!string.IsNullOrWhiteSpace(appPath));

                        foreach(var assemblyPath in Directory.GetFiles(appPath, "*.dll"))
                        {
                            _externalAssemblies.Add(Assembly.LoadFrom(assemblyPath));
                        }
                    }

                    return _externalAssemblies;
                }
            }
        }
    }
}

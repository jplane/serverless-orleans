using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Orleans;
using Orleans.Hosting;
using Orleans.Configuration;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace ServerlessOrleans
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure((ctx, app) =>
                {
                    if (ctx.HostingEnvironment.IsEnvironment("Local-InMemory") ||
                        ctx.HostingEnvironment.IsEnvironment("Local-SQL"))
                    {
                        app.UseDeveloperExceptionPage();
                    }

                    app.UseHttpsRedirection();
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                    });
                });
            });

            builder.ConfigureWebJobs(webJobsBuilder =>
            {
                webJobsBuilder.AddAzureStorageCoreServices();
                webJobsBuilder.AddEventGrid();
                webJobsBuilder.AddAzureStorage();
            });

            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.AddConsole();
            });

            builder.ConfigureServices(services =>
            {
                services.AddControllers();
            });

            builder.UseOrleans((ctxt, siloBuilder) =>
            {
                siloBuilder = siloBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "serverlessorleans";
                    options.ServiceId = "serverlessorleans";
                });

                if (ctxt.HostingEnvironment.IsEnvironment("Local-InMemory"))
                {
                    siloBuilder.AddMemoryGrainStorageAsDefault()
                               .UseLocalhostClustering();
                }
                else if (ctxt.HostingEnvironment.IsEnvironment("Local-SQL"))
                {
                    throw new NotImplementedException();
                }
                else if (ctxt.HostingEnvironment.IsEnvironment("Azure-Storage"))
                {
                    siloBuilder
                        .AddAzureBlobGrainStorageAsDefault(options =>
                        {
                            options.ConnectionString = ctxt.Configuration["AzureWebJobsStorage"];
                            options.UseJson = true;
                            options.ContainerName = "actorstate";
                        })
                        .UseAzureStorageClustering(options =>
                        {
                            options.ConnectionString = ctxt.Configuration["AzureWebJobsStorage"];
                            options.TableName = "clusterstate";
                        })
                        .ConfigureEndpoints(siloPort: 11111, gatewayPort: 30000);
                }
                else if (ctxt.HostingEnvironment.IsEnvironment("Azure-SQL"))
                {
                    throw new NotImplementedException();
                }
            });

            return builder.RunConsoleAsync();
        }
    }
}

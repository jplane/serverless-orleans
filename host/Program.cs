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

namespace ServerlessOrleans
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            //builder.UseEnvironment(Environments.Development);

            builder.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure((ctx, app) =>
                {
                    if (ctx.HostingEnvironment.IsDevelopment())
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

            builder.UseOrleans(siloBuilder =>
            {
                siloBuilder.AddMemoryGrainStorage("main")
                           .UseLocalhostClustering()
                           .Configure<ClusterOptions>(opts =>
                           {
                               opts.ClusterId = "dev";
                               opts.ServiceId = "MessagesAPI";
                           })
                           .Configure<EndpointOptions>(opts =>
                           {
                               opts.AdvertisedIPAddress = IPAddress.Loopback;
                           });
            });

            return builder.RunConsoleAsync();
        }
    }
}

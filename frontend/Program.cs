using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using Orleans;

namespace Frontend
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<ActorClientService>();
                        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<ActorClientService>());
                        services.AddSingleton<IClusterClient>(sp => sp.GetRequiredService<ActorClientService>().Client);
                        services.AddSingleton<IGrainFactory>(sp => sp.GetRequiredService<IClusterClient>());

                        services.AddServicesForSelfHostedDashboard();
                    })
                    .Configure((ctx, app) =>
                    {
                        if (ctx.HostingEnvironment.IsDevelopment())
                        {
                            app.UseDeveloperExceptionPage();
                        }

                        app.UseOrleansDashboard(new OrleansDashboard.DashboardOptions { BasePath = "/dashboard" });
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

            return builder.RunConsoleAsync();
        }
    }
}

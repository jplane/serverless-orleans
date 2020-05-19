using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;

namespace Frontend
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var builder = Host.CreateDefaultBuilder(args);

            var env = Environment.GetEnvironmentVariable("ENVIRONMENT");

            builder.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure((ctx, app) =>
                {
                    if (env == "LOCAL-INMEMORY" || env == "LOCAL-SQL")
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

                services.AddSingleton<ActorClientService>();
                services.AddSingleton<IHostedService>(_ => _.GetService<ActorClientService>());
                services.AddSingleton(_ => _.GetService<ActorClientService>().Client);
            });

            return builder.RunConsoleAsync();
        }
    }
}

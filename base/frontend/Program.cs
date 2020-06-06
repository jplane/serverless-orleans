using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Linq;
using Microsoft.Azure.WebJobs;
using System.Collections.Generic;
using System.Reflection;
using System;

namespace Frontend
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            var externalAssemblies = ActorClientService.ExternalAssemblies;

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
                webJobsBuilder.AddAzureStorage();

                InjectWebJobTypeLocator(webJobsBuilder, externalAssemblies);
            });

            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.AddConsole();
            });

            builder.ConfigureServices(services =>
            {
                var mvcBuilder = services.AddControllers();

                foreach(var assembly in externalAssemblies)
                {
                    mvcBuilder.AddApplicationPart(assembly);
                }
            });

            return builder.RunConsoleAsync();
        }

        private static void InjectWebJobTypeLocator(IWebJobsBuilder webJobsBuilder,
                                                    IEnumerable<Assembly> externalAssemblies)
        {
            var locatorType = typeof(ITypeLocator).FullName;
            
            var type = externalAssemblies.SelectMany(a => a.GetTypes())
                                         .SingleOrDefault(t => t.IsPublic && t.GetInterface(locatorType) != null);

            if (type == null)
            {
                throw new Exception("Unable to resolve single public implementation of type: " + locatorType);
            }

            var locator = (ITypeLocator) Activator.CreateInstance(type);

            var existingTypeResolverDescriptor = webJobsBuilder
                                                    .Services
                                                    .Where(d => d.ServiceType == typeof(ITypeLocator))
                                                    .ToArray();
                
            Array.ForEach(existingTypeResolverDescriptor, d => webJobsBuilder.Services.Remove(d));

            webJobsBuilder.Services.AddSingleton<ITypeLocator>(locator);
        }
    }
}

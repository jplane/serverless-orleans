using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ContainerRegistry.Fluent;
using Nito.AsyncEx;

namespace Frontend
{
    [ApiController]
    [Route("mgmt")]
    public class ManagementController : ControllerBase
    {
        private static Random _random = new Random(Environment.TickCount);
        private static AsyncLock _mutex = new AsyncLock();
        
        private readonly IConfiguration _config;
        private readonly IHostEnvironment _hostEnv;
        private readonly ILogger<ManagementController> _log;

        public ManagementController(IConfiguration config,
                                    IHostEnvironment hostEnv,
                                    ILogger<ManagementController> log)
        {
            _config = config;
            _hostEnv = hostEnv;
            _log = log;
        }

        [HttpPost("scaleout")]
        public async Task ScaleOut()
        {
            _log.LogInformation("Scaling out the actor cluster");

            var azure = await GetAzureContext();

            var name = Environment.GetEnvironmentVariable("ACG_ROOT_NAME");
            var rg = $"{name}-rg";

            var aci_name = $"{name}cg{GetRandomString(4)}";
            var acr_uri = $"$https://{name}registry.azurecr.io";
            var acr_user = $"{name}registry";

            // get existing ACI instance
            // there is no means to query network profiles via .NET SDK <sigh>
            //  also, when you get a profile ID from an existing container it comes as a single string,
            //  and below we need it in separate chunks <sigh> <sigh>
            var existingContainerGroup = (await azure.ContainerGroups.ListByResourceGroupAsync(rg)).First();

            var profileName = existingContainerGroup.NetworkProfileId.Split("/").Last();
            var la_wksp_id = existingContainerGroup.LogAnalytics.WorkspaceId;
            var la_wksp_key = existingContainerGroup.LogAnalytics.WorkspaceKey;
            
            var existingContainerInstance = existingContainerGroup.Containers.Single();
            
            var ports = existingContainerInstance.Value.Ports.Select(p => p.Port).ToArray();
            var image = existingContainerInstance.Value.Image;
            var env_vars = existingContainerInstance.Value.EnvironmentVariables.ToDictionary(e => e.Name, e => e.Value);
            var cpu = existingContainerInstance.Value.Resources.Requests.Cpu;
            var ram = existingContainerInstance.Value.Resources.Requests.MemoryInGB;

            // get ACR password
            var existingRegistry = await azure.ContainerRegistries.GetByResourceGroupAsync(rg, acr_user);
            var acr_pwd = (await existingRegistry.GetCredentialsAsync()).AccessKeys[AccessKeyType.Primary];

            await azure.ContainerGroups
                            .Define(aci_name)
                            .WithRegion(existingContainerGroup.Region)
                            .WithExistingResourceGroup(rg)
                            .WithLinux()
                            .WithPrivateImageRegistry(acr_uri, acr_user, acr_pwd)
                            .WithoutVolume()
                            .DefineContainerInstance(aci_name)
                                .WithImage(image)
                                .WithExternalTcpPorts(ports)
                                .WithCpuCoreCount(cpu)
                                .WithMemorySizeInGB(ram)
                                .WithEnvironmentVariables(env_vars)
                                .Attach()
                            .WithLogAnalytics(la_wksp_id, la_wksp_key)
                            .WithNetworkProfileId(azure.SubscriptionId, rg, profileName)
                            .CreateAsync();
        }

        [HttpPost("scalein")]
        public async Task ScaleIn()
        {
            _log.LogInformation("Scaling in the actor cluster");

            var azure = await GetAzureContext();

            var name = Environment.GetEnvironmentVariable("ACG_ROOT_NAME");
            var rg = $"{name}-rg";

            using (await _mutex.LockAsync())
            {
                var existingGroups = await azure.ContainerGroups.ListByResourceGroupAsync(rg);

                if (existingGroups.Count() > 1)
                {
                    var group = existingGroups.First();

                    await group.StopAsync();
                    await azure.ContainerGroups.DeleteByIdAsync(group.Id);
                }
            }
        }

        private string GetRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            return new string(
                    Enumerable.Repeat(chars, length)
                              .Select(s => s[_random.Next(s.Length)])
                              .ToArray());
        }

        private async Task<IAzure> GetAzureContext()
        {
            var creds = GetAzureCredentials();
            return await Azure.Authenticate(creds).WithDefaultSubscriptionAsync();
        }

        private AzureCredentials GetAzureCredentials()
        {
            var factory = new AzureCredentialsFactory();

            if (_hostEnv.IsDevelopment())
            {
                throw new NotSupportedException();
            }
            else
            {
                return factory.FromSystemAssignedManagedServiceIdentity(MSIResourceType.AppService,
                                                                        AzureEnvironment.AzureGlobalCloud);
            }
        }
    }
}

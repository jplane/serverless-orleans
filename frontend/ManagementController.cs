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
using Microsoft.Azure.Management.ContainerInstance.Fluent;

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

            var name = _config["ACG_ROOT_NAME"];
            var rg = $"{name}-rg";

            var aci_name = $"{name}cg{GetRandomString(4)}";
            var acr_uri = $"{name}registry.azurecr.io";
            var acr_user = $"{name}registry";
            var la_name = $"{name}loganalyticsworkspace";

            await CreateNewAci(azure, rg, aci_name, acr_uri, acr_user);
            await CreateNewMetricsOutput(azure, rg, aci_name, la_name);
        }

        [HttpPost("scalein")]
        public async Task ScaleIn()
        {
            _log.LogInformation("Scaling in the actor cluster");

            var azure = await GetAzureContext();

            var name = _config["ACG_ROOT_NAME"];
            var rg = $"{name}-rg";

            using (await _mutex.LockAsync())
            {
                var existingGroups = await azure.ContainerGroups.ListByResourceGroupAsync(rg);

                if (existingGroups.Count() > 1)
                {
                    var group = existingGroups.Where(g => ! g.Name.EndsWith("cg1234")).First();

                    await group.StopAsync();
                    await RemoveMetricsOutput(azure, rg, group.Name);
                    await azure.ContainerGroups.DeleteByIdAsync(group.Id);
                }
            }
        }

        private async Task RemoveMetricsOutput(IAzure azure, string rg, string aci_name)
        {
            var resourceId = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/Microsoft.ContainerInstance/containerGroups/{aci_name}";
            var diagnosticSettingName = $"{aci_name}metricsoutput";

            await azure.DiagnosticSettings.DeleteAsync(resourceId, diagnosticSettingName);
        }

        private async Task CreateNewMetricsOutput(IAzure azure, string rg, string aci_name, string la_name)
        {
            var la_resource_Id = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/microsoft.operationalinsights/workspaces/{la_name}";
            var resourceId = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/Microsoft.ContainerInstance/containerGroups/{aci_name}";

            await azure.DiagnosticSettings
                            .Define($"{aci_name}metricsoutput")
                            .WithResource(resourceId)
                            .WithLogAnalytics(la_resource_Id)
                                .WithMetric("AllMetrics", TimeSpan.FromMinutes(1), 7)
                            .CreateAsync();
        }

        private async Task CreateNewAci(IAzure azure, string rg, string aci_name, string acr_uri, string acr_user)
        {
            // get existing ACI instance
            // there is no means to query network profiles via .NET SDK <sigh>
            //  also, when you get a profile ID from an existing container it comes as a single string,
            //  and below we need it in separate chunks <sigh> <sigh>
            var existingContainerGroup = (await GetRootActorContainerGroup(azure, rg));

            var profileName = existingContainerGroup.NetworkProfileId.Split("/").Last();
            var existingContainerInstance = existingContainerGroup.Containers.Single();
            var ports = existingContainerInstance.Value.Ports.Select(p => p.Port).ToArray();
            var image = existingContainerInstance.Value.Image;
            var env_vars = existingContainerInstance.Value.EnvironmentVariables.ToDictionary(e => e.Name, e => e.Value);
            var cpu = existingContainerInstance.Value.Resources.Requests.Cpu;
            var ram = existingContainerInstance.Value.Resources.Requests.MemoryInGB;

            // get ACR password
            var existingRegistry = await azure.ContainerRegistries.GetByResourceGroupAsync(rg, acr_user);
            var acr_pwd = (await existingRegistry.GetCredentialsAsync()).AccessKeys[AccessKeyType.Primary];

            var la_workspace_id = _config["LOG_ANALYTICS_WORKSPACE_ID"];
            var la_workspace_key = _config["LOG_ANALYTICS_WORKSPACE_KEY"];

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
                            .WithLogAnalytics(la_workspace_id, la_workspace_key)
                            .WithNetworkProfileId(azure.SubscriptionId, rg, profileName)
                            .CreateAsync();
        }

        private Task<IContainerGroup> GetRootActorContainerGroup(IAzure azure, string resourceGroup)
        {
            var name = _config["ACG_ROOT_NAME"];
            var cg_name = $"{name}cg1234";

            return azure.ContainerGroups.GetByResourceGroupAsync(resourceGroup, cg_name);
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
                return factory.FromFile("./azureauth.json");
            }
            else
            {
                var tenantId = _config["SERVICE_PRINCIPAL_TENANT_ID"];
                var servicePrincipalId = _config["SERVICE_PRINCIPAL_ID"];
                var servicePrincipalSecret = _config["SERVICE_PRINCIPAL_SECRET"];

                return factory.FromServicePrincipal(servicePrincipalId,
                                                    servicePrincipalSecret,
                                                    tenantId,
                                                    AzureEnvironment.AzureGlobalCloud);
            }
        }
    }
}

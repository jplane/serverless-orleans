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
using System.Diagnostics;

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
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (hostEnv == null)
            {
                throw new ArgumentNullException(nameof(hostEnv));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            _config = config;
            _hostEnv = hostEnv;
            _log = log;
        }

        [HttpPost("scaleout")]
        public async Task ScaleOut()
        {
            Debug.Assert(_log != null);
            
            _log.LogInformation("Scaling out the actor cluster");

            Debug.Assert(_config != null);

            var name = _config["ACG_ROOT_NAME"];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Unable to resolve CONTAINER GROUP NAME from configuration.");
            }

            var rg = $"{name}-rg";

            var aci_name = $"{name}cg{GetRandomString(4)}";
            var acr_uri = $"{name}registry.azurecr.io";
            var acr_user = $"{name}registry";
            var la_name = $"{name}loganalyticsworkspace";

            var azure = await GetAzureContext().ConfigureAwait(false);

            Debug.Assert(azure != null);

            await CreateNewAci(azure, rg, aci_name, acr_uri, acr_user).ConfigureAwait(false);
            await CreateNewMetricsOutput(azure, rg, aci_name, la_name).ConfigureAwait(false);
        }

        [HttpPost("scalein")]
        public async Task ScaleIn()
        {
            Debug.Assert(_log != null);

            _log.LogInformation("Scaling in the actor cluster");

            var name = _config["ACG_ROOT_NAME"];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Unable to resolve CONTAINER GROUP NAME from configuration.");
            }

            var rg = $"{name}-rg";

            using (await _mutex.LockAsync())
            {
                var azure = await GetAzureContext().ConfigureAwait(false);

                Debug.Assert(azure != null);
                Debug.Assert(azure.ContainerGroups != null);

                var existingGroups = await azure.ContainerGroups.ListByResourceGroupAsync(rg).ConfigureAwait(false);

                if (existingGroups == null)
                {
                    throw new InvalidOperationException("Unable to resolve existing ACI containe groups from Azure.");
                }

                if (existingGroups.Count() > 1)
                {
                    var group = existingGroups.Where(g => ! g.Name.EndsWith("cg1234")).First();

                    Debug.Assert(group != null);

                    await group.StopAsync().ConfigureAwait(false);
                    await RemoveMetricsOutput(azure, rg, group.Name).ConfigureAwait(false);
                    await azure.ContainerGroups.DeleteByIdAsync(group.Id).ConfigureAwait(false);
                }
            }
        }

        private async Task RemoveMetricsOutput(IAzure azure, string rg, string aci_name)
        {
            Debug.Assert(azure != null);
            Debug.Assert(!string.IsNullOrWhiteSpace(rg));
            Debug.Assert(!string.IsNullOrWhiteSpace(aci_name));

            var resourceId = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/Microsoft.ContainerInstance/containerGroups/{aci_name}";
            var diagnosticSettingName = $"{aci_name}metricsoutput";

            Debug.Assert(azure.DiagnosticSettings != null);

            await azure.DiagnosticSettings.DeleteAsync(resourceId, diagnosticSettingName).ConfigureAwait(false);
        }

        private async Task CreateNewMetricsOutput(IAzure azure, string rg, string aci_name, string la_name)
        {
            Debug.Assert(azure != null);
            Debug.Assert(!string.IsNullOrWhiteSpace(rg));
            Debug.Assert(!string.IsNullOrWhiteSpace(aci_name));
            Debug.Assert(!string.IsNullOrWhiteSpace(la_name));

            var la_resource_Id = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/microsoft.operationalinsights/workspaces/{la_name}";
            var resourceId = $"/subscriptions/{azure.SubscriptionId}/resourcegroups/{rg}/providers/Microsoft.ContainerInstance/containerGroups/{aci_name}";

            Debug.Assert(azure.DiagnosticSettings != null);

            await azure.DiagnosticSettings
                            .Define($"{aci_name}metricsoutput")
                            .WithResource(resourceId)
                            .WithLogAnalytics(la_resource_Id)
                                .WithMetric("AllMetrics", TimeSpan.FromMinutes(1), 7)
                            .CreateAsync()
                            .ConfigureAwait(false);
        }

        private async Task CreateNewAci(IAzure azure, string rg, string aci_name, string acr_uri, string acr_user)
        {
            Debug.Assert(azure != null);
            Debug.Assert(!string.IsNullOrWhiteSpace(rg));
            Debug.Assert(!string.IsNullOrWhiteSpace(aci_name));
            Debug.Assert(!string.IsNullOrWhiteSpace(acr_uri));
            Debug.Assert(!string.IsNullOrWhiteSpace(acr_user));

            // get existing ACI instance
            // there is no means to query network profiles via .NET SDK <sigh>
            //  also, when you get a profile ID from an existing container it comes as a single string,
            //  and below we need it in separate chunks <sigh> <sigh>
            var existingContainerGroup = (await GetRootActorContainerGroup(azure, rg).ConfigureAwait(false));

            if (existingContainerGroup == null)
            {
                throw new InvalidOperationException("Unable to resolve existing container group in Azure.");
            }

            Debug.Assert(existingContainerGroup.NetworkProfileId != null);
            Debug.Assert(existingContainerGroup.Containers != null);

            var profileName = existingContainerGroup.NetworkProfileId.Split("/").Last();
            var existingContainerInstance = existingContainerGroup.Containers.Single();

            if (existingContainerInstance.Value == null)
            {
                throw new InvalidOperationException("Unable to resolve existing container instance in Azure.");
            }

            Debug.Assert(existingContainerInstance.Value.Ports != null);
            Debug.Assert(existingContainerInstance.Value.Image != null);
            Debug.Assert(existingContainerInstance.Value.EnvironmentVariables != null);
            Debug.Assert(existingContainerInstance.Value.Resources != null);
            Debug.Assert(existingContainerInstance.Value.Resources.Requests != null);

            var ports = existingContainerInstance.Value.Ports.Select(p => p.Port).ToArray();
            var image = existingContainerInstance.Value.Image;
            var env_vars = existingContainerInstance.Value.EnvironmentVariables.ToDictionary(e => e.Name, e => e.Value);
            var cpu = existingContainerInstance.Value.Resources.Requests.Cpu;
            var ram = existingContainerInstance.Value.Resources.Requests.MemoryInGB;

            Debug.Assert(azure.ContainerRegistries != null);

            // get ACR password
            var existingRegistry = await azure.ContainerRegistries.GetByResourceGroupAsync(rg, acr_user).ConfigureAwait(false);

            if (existingRegistry == null)
            {
                throw new InvalidOperationException("Unable to resolve existing container registry in Azure.");
            }

            var acr_pwd = (await existingRegistry.GetCredentialsAsync().ConfigureAwait(false)).AccessKeys[AccessKeyType.Primary];

            Debug.Assert(_config != null);

            var la_workspace_id = _config["LOG_ANALYTICS_WORKSPACE_ID"];
            var la_workspace_key = _config["LOG_ANALYTICS_WORKSPACE_KEY"];

            Debug.Assert(azure.ContainerGroups != null);

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
                            .CreateAsync()
                            .ConfigureAwait(false);
        }

        private Task<IContainerGroup> GetRootActorContainerGroup(IAzure azure, string resourceGroup)
        {
            Debug.Assert(azure != null);
            Debug.Assert(!string.IsNullOrWhiteSpace(resourceGroup));

            Debug.Assert(_config != null);

            var name = _config["ACG_ROOT_NAME"];
            var cg_name = $"{name}cg1234";

            Debug.Assert(azure.ContainerGroups != null);

            return azure.ContainerGroups.GetByResourceGroupAsync(resourceGroup, cg_name);
        }

        private string GetRandomString(int length)
        {
            Debug.Assert(length > 0);

            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";

            return new string(
                    Enumerable.Repeat(chars, length)
                              .Select(s => s[_random.Next(s.Length)])
                              .ToArray());
        }

        private async Task<IAzure> GetAzureContext()
        {
            var creds = GetAzureCredentials();

            Debug.Assert(creds != null);

            return await Azure.Authenticate(creds).WithDefaultSubscriptionAsync().ConfigureAwait(false);
        }

        private AzureCredentials GetAzureCredentials()
        {
            var factory = new AzureCredentialsFactory();

            Debug.Assert(factory != null);
            Debug.Assert(_hostEnv != null);
            
            if (_hostEnv.IsDevelopment())
            {
                return factory.FromFile("./azureauth.json");
            }
            else
            {
                Debug.Assert(_config != null);

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

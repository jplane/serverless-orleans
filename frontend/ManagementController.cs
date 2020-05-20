using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Actors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ContainerInstance.Fluent;
using Microsoft.Azure.Management.ContainerInstance.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.Network.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;

namespace Frontend
{
    [ApiController]
    [Route("mgmt")]
    public class ManagementController : ControllerBase
    {
        private static Random _random = new Random(Environment.TickCount);
        private readonly IConfiguration _config;
        private readonly ILogger<ManagementController> _log;

        public ManagementController(IConfiguration config, ILogger<ManagementController> log)
        {
            _config = config;
            _log = log;
        }

        [HttpPost("scaleup")]
        public async Task ScaleUp()
        {
            _log.LogInformation("Scaling up actor cluster");

            var azure = GetAzureContext();

            var name = $"{_config["aci:name"]}cg{GetRandomString(4)}";
            var region = (Region) Enum.Parse(typeof(Region), _config["aci:region"], true);
            var rg = _config["aci:rg"];
            var acr_uri = _config["aci:acr_uri"];
            var acr_user = _config["aci:acr_user"];
            var acr_pwd = _config["aci:acr_pwd"];
            var image_name = _config["aci:image_name"];
            var la_wksp_id = _config["aci:la_wksp_id"];
            var la_wksp_key = _config["aci:la_wksp_key"];
            var storage_connection_string = _config["aci:storage_connection_string"];
            var subscription = _config["aci:sub_id"];

            // assumes at least one already exists...
            var existingContainerGroup = (await azure.ContainerGroups.ListByResourceGroupAsync(rg)).First();

            // there is no means to query network profiles via .NET SDK <sigh>
            //  also, when you get a profile ID from an existing container it comes as a single string,
            //  and below we need it in separate chunks <sigh> <sigh>
            var profileName = existingContainerGroup.NetworkProfileId.Split("/").Last();

            await azure.ContainerGroups
                            .Define(name)
                            .WithRegion(region)
                            .WithExistingResourceGroup(rg)
                            .WithLinux()
                            .WithPrivateImageRegistry(acr_uri, acr_user, acr_pwd)
                            .WithoutVolume()
                            .DefineContainerInstance(name + "-1")
                                .WithImage(image_name)
                                .WithExternalTcpPorts(11111, 30000)
                                .WithCpuCoreCount(1.0)
                                .WithMemorySizeInGB(1.5)
                                .WithEnvironmentVariable("ORLEANS_CONFIG", "STORAGE")
                                .WithEnvironmentVariable("StorageConnectionString", storage_connection_string)
                                .Attach()
                            .WithLogAnalytics(la_wksp_id, la_wksp_key)
                            .WithNetworkProfileId(subscription, rg, profileName)
                            .CreateAsync();

            await UpdateAutoscaleScopes();
        }

        [HttpPost("scaledown")]
        public async Task ScaleDown()
        {
            _log.LogInformation("Scaling down actor cluster");

            var azure = GetAzureContext();

            var rg = _config["aci:rg"];

            var existingContainerGroup = (await azure.ContainerGroups.ListByResourceGroupAsync(rg)).FirstOrDefault();

            if (existingContainerGroup != null)
            {
                await existingContainerGroup.StopAsync();
                await azure.ContainerGroups.DeleteByIdAsync(existingContainerGroup.Id);
                await UpdateAutoscaleScopes();
            }
        }

        private async Task UpdateAutoscaleScopes()
        {
            // get list of container groups in resource group
            // update scaleup alert scope to list of IDs
            // update scaledown alert scope to list of IDs
        }

        private string GetRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            return new string(
                    Enumerable.Repeat(chars, length)
                              .Select(s => s[_random.Next(s.Length)])
                              .ToArray());
        }

        private IAzure GetAzureContext()
        {
            var sp = _config["aci:sp"];
            var key = _config["aci:sp_key"];
            var tenant = _config["aci:tenant_id"];
            var subscription = _config["aci:sub_id"];

            var creds = new AzureCredentialsFactory()
                                .FromServicePrincipal(sp, key, tenant, AzureEnvironment.AzureGlobalCloud);
            
            return Azure.Authenticate(creds).WithSubscription(subscription);
        }
    }
}

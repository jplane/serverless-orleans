using System;
using System.Linq;
using System.Threading.Tasks;
using Actors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Frontend
{
    [ApiController]
    [Route("messages")]
    public class MessagesController : ControllerBase
    {
        private readonly IClusterClient _client;
        private readonly ILogger<MessagesController> _log;

        public MessagesController(IClusterClient client, ILogger<MessagesController> log)
        {
            _client = client;
            _log = log;
        }

        [HttpGet]
        public async Task<string[]> GetMessages()
        {
            _log.LogInformation("Getting messages");

            var actor = _client.GetGrain<IMessageActor>(0);

            var messages = await actor.GetMessages();

            return messages.ToArray();
        }
    }
}

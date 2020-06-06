using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;

namespace MessageApp
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
        [Route("{actorId}")]
        public async Task<string[]> GetMessages(long actorId)
        {
            _log.LogInformation("Getting messages");

            var actor = _client.GetGrain<IMessageActor>(actorId);

            var messages = await actor.GetMessages().ConfigureAwait(false);

            return messages.ToArray();
        }

        [HttpPost]
        [Route("{actorId}")]
        public async Task AddMessage(long actorId, [FromBody] string message)
        {
            _log.LogInformation("Adding a message");

            var actor = _client.GetGrain<IMessageActor>(actorId);

            await actor.AddMessage(message).ConfigureAwait(false);
        }
    }
}

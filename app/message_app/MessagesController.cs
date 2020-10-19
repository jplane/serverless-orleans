using System;
using System.Diagnostics;
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
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            _client = client;
            _log = log;
        }

        [HttpGet]
        [Route("{actorId}")]
        public async Task<string[]> GetMessages(long actorId)
        {
            Debug.Assert(_log != null);
            
            _log.LogInformation("Getting messages");

            Debug.Assert(_client != null);

            var actor = GetActor(actorId);

            Debug.Assert(actor != null);

            var messages = await actor.GetMessages().ConfigureAwait(false);

            if (messages == null)
            {
                throw new InvalidOperationException("Actor failed to return valid list of messages.");
            }

            return messages.ToArray();
        }

        [HttpPost]
        [Route("{actorId}")]
        public async Task AddMessage(long actorId, [FromBody] string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("'Message' argument is not valid.");
            }

            Debug.Assert(_log != null);

            _log.LogInformation("Adding a message");

            var actor = GetActor(actorId);

            Debug.Assert(actor != null);

            await actor.AddMessage(message).ConfigureAwait(false);
        }

        private IMessageActor GetActor(long actorId)
        {
            Debug.Assert(_client != null);

            var actor = _client.GetGrain<IMessageActor>(actorId);

            if (actor == null)
            {
                throw new InvalidOperationException("Orleans runtime failed to return valid actor instance.");
            }

            return actor;
        }
    }
}

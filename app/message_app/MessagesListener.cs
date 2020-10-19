using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using Orleans;

namespace MessageApp
{
    public class MessagesListener
    {
        private readonly IClusterClient _client;
        private readonly ILogger<MessagesListener> _log;

        public MessagesListener(IClusterClient client, ILogger<MessagesListener> log)
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

        public async Task ProcessQueueMessage([QueueTrigger("input")] CloudQueueMessage item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Debug.Assert(_log != null);
            Debug.Assert(item.Id != null);

            _log.LogInformation("Queue message received: " + item.Id);

            Debug.Assert(!string.IsNullOrWhiteSpace(item.AsString));

            var msg = JsonConvert.DeserializeAnonymousType(item.AsString, new { actorId = 0L, message = "" });

            if (msg == null)
            {
                throw new InvalidOperationException("Unable to convert CloudQueueMessage JSON body to object.");
            }

            Debug.Assert(_client != null);

            var actor = _client.GetGrain<IMessageActor>(msg.actorId);

            if (actor == null)
            {
                throw new InvalidOperationException("Orleans runtime failed to return valid actor instance.");
            }

            await actor.AddMessage(msg.message).ConfigureAwait(false);
        }
    }
}

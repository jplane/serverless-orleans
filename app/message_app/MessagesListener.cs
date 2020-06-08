using System;
using System.Collections.Generic;
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
            _client = client;
            _log = log;
        }

        public async Task ProcessQueueMessage([QueueTrigger("input")] CloudQueueMessage item)
        {
            _log.LogInformation("Queue message received: " + item.Id);

            var msg = JsonConvert.DeserializeAnonymousType(item.AsString, new { actorId = 0L, message = "" });

            var actor = _client.GetGrain<IMessageActor>(msg.actorId);

            await actor.AddMessage(msg.message).ConfigureAwait(false);
        }
    }
}

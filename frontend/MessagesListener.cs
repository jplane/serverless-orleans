using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Actors;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Orleans;

namespace Frontend
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

        public async Task ProcessQueueMessage([QueueTrigger("input")] CloudQueueMessage message)
        {
            _log.LogInformation("Queue message received: " + message.Id);

            var actor = _client.GetGrain<IMessageActor>(0);

            await actor.AddMessage(message.AsString);
        }
    }
}

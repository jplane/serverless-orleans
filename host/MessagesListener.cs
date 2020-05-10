using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Orleans;

namespace ServerlessOrleans
{
    public class MessagesListener
    {
        private readonly IMessageActor _actor;
        private readonly ILogger<MessagesListener> _log;

        public MessagesListener(IGrainFactory client, ILogger<MessagesListener> log)
        {
            _actor = client.GetGrain<IMessageActor>(0);
            _log = log;
        }

        public async Task ProcessQueueMessage([QueueTrigger("input")] CloudQueueMessage message)
        {
            _log.LogInformation("Queue message received: " + message.Id);

            await _actor.AddMessage(message.AsString);
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;

namespace ServerlessOrleans
{
    [ApiController]
    [Route("api/messages")]
    public class MessagesController : ControllerBase
    {
        private readonly IMessageActor _grain;
        private readonly ILogger<MessagesController> _log;

        public MessagesController(IGrainFactory client, ILogger<MessagesController> log)
        {
            _grain = client.GetGrain<IMessageActor>(0);
            _log = log;
        }

        [HttpGet]
        public async Task<string> GetMessages()
        {
            _log.LogInformation("Getting messages");

            var messages = await _grain.GetMessages();

            return string.Join(Environment.NewLine, messages);
        }
    }
}

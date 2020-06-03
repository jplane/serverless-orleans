using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Actors
{
    public class MessageActor : Orleans.Grain, IMessageActor
    {
        private readonly IPersistentState<MessageState> _state;
        private readonly ILogger<MessageActor> _log;

        public MessageActor([PersistentState("state")] IPersistentState<MessageState> state,
                            ILogger<MessageActor> log)
        {
            _state = state;
            _log = log;
        }

        public async Task AddMessage(string message)
        {
            _log.LogInformation("Adding message: " + message);

            _state.State.Messages.Add(message);

            await _state.WriteStateAsync().ConfigureAwait(false);
        }

        public Task<IEnumerable<string>> GetMessages()
        {
            _log.LogInformation("Reading messages");

            return Task.FromResult(_state.State.Messages.AsEnumerable());
        }
    }

    [Serializable]
    public class MessageState
    {
        public List<string> Messages = new List<string>();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace ServerlessOrleans
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

            _state.State.AddMessage(message);

            await _state.WriteStateAsync();
        }

        public Task<IEnumerable<string>> GetMessages()
        {
            _log.LogInformation("Reading messages");

            return Task.FromResult(_state.State.GetMessages());
        }
    }

    [Serializable]
    public class MessageState
    {
        private readonly List<string> _messages = new List<string>();

        public void AddMessage(string message) => _messages.Add(message);

        public IEnumerable<string> GetMessages() => _messages;
    }
}
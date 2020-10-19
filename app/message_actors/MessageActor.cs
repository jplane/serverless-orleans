using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace MessageApp
{
    public class MessageActor : Orleans.Grain, IMessageActor
    {
        private readonly IPersistentState<MessageState> _state;
        private readonly ILogger<MessageActor> _log;

        public MessageActor([PersistentState("state")] IPersistentState<MessageState> state,
                            ILogger<MessageActor> log)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            _state = state;
            _log = log;
        }

        public async Task AddMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("'Message' argument is not valid.");
            }

            Debug.Assert(_log != null);

            _log.LogInformation("Adding message: " + message);

            Debug.Assert(_state != null);
            Debug.Assert(_state.State != null);
            Debug.Assert(_state.State.Messages != null);

            _state.State.Messages.Add(message);

            await _state.WriteStateAsync().ConfigureAwait(false);
        }

        public Task<IEnumerable<string>> GetMessages()
        {
            Debug.Assert(_log != null);

            _log.LogInformation("Reading messages");

            Debug.Assert(_state != null);
            Debug.Assert(_state.State != null);
            Debug.Assert(_state.State.Messages != null);

            return Task.FromResult(_state.State.Messages.AsEnumerable());
        }
    }

    [Serializable]
    public class MessageState
    {
        public List<string> Messages = new List<string>();
    }
}

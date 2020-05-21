using System.Collections.Generic;
using System.Threading.Tasks;

namespace Actors
{
    public interface IMessageActor : Orleans.IGrainWithIntegerKey
    {
        Task AddMessage(string message);
        Task<IEnumerable<string>> GetMessages();
    }
}

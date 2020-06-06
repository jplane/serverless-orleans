using System.Collections.Generic;
using System.Threading.Tasks;

namespace MessageApp
{
    public interface IMessageActor : Orleans.IGrainWithIntegerKey
    {
        Task AddMessage(string message);
        Task<IEnumerable<string>> GetMessages();
    }
}

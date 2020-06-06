using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;

namespace MessageApp
{
    public class WebJobTypeLocator : ITypeLocator
    {
        public WebJobTypeLocator()
        {
        }

        public IReadOnlyList<Type> GetTypes()
        {
            return new List<Type>
            {
                typeof(MessagesListener)
            };
        }
    }
}
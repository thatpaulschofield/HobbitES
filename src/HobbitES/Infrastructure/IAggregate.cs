using System;
using System.Collections.Generic;

namespace HobbitES.Infrastructure
{
    public interface IAggregate
    {
        void ApplyEvent(object evt);
        IEnumerable<object> GetUncommittedEvents();
        int Version { get; }
        Guid Id { get; }
        void ClearUncommittedEvents();
    }
}
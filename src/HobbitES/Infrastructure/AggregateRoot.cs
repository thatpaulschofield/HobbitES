using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HobbitES.Infrastructure
{
    public class Aggregate : IAggregate
    {
        private List<object> _uncommittedEvents = new List<object>();

        public void ApplyEvent(object evt)
        {
            ((dynamic) this).ApplyEvent(evt);
            Version++;
        }

        public IEnumerable<object> GetUncommittedEvents()
        {
            return _uncommittedEvents.AsEnumerable();
        }

        public int Version { get; private set; }
        public Guid Id { get; set; }
        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }
    }
}

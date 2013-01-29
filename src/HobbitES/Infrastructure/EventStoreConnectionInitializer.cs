using EventStore.ClientAPI;
using NServiceBus;

namespace HobbitES.Infrastructure
{
    public class EventStoreConnectionInitializer : IWantCustomInitialization
    {
        public void Init()
        {
            Configure.Instance.Configurer.ConfigureComponent<EventStoreConnection>(EventStoreConnection.Create,
                                                                                   DependencyLifecycle.SingleInstance);
        }
    }
}
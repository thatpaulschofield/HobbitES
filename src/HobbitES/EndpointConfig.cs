using HobbitES.Infrastructure;
using NServiceBus.Hosting.Profiles;

namespace HobbitES
{
    using NServiceBus;

    /*
        This class configures this endpoint as a Server. More information about how to configure the NServiceBus host
        can be found here: http://nservicebus.com/GenericHost.aspx
    */

    public class EndpointConfig : IConfigureThisEndpoint, AsA_Server, IWantCustomInitialization
    {
        public void Init()
        {
            NServiceBus.Configure.With(AllAssemblies.Except("HobbitES.VSHost.exe"))
                       .DefaultBuilder()
                       .JsonSerializer()
                       .MsmqTransport()
                       .Sagas()
                       .DefiningCommandsAs(t => t.Namespace != null && t.Namespace.EndsWith("Commands"))
                       .DefiningEventsAs(t => t.Namespace != null && t.Namespace.EndsWith("Events"));
        }
    }

    public class MyProfile : NServiceBus.IProfile
    {

    }

    public class MyProfileHandler : IHandleProfile<MyProfile>
    {
        public void ProfileActivated()
        {
            Configure.Instance
                     .EventStoreSagaPersistence()
                     .EventStoreRepository();
        }

    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using NServiceBus;

namespace HobbitES.Web
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();

            WebApiConfig.Register(GlobalConfiguration.Configuration);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            Bus = NServiceBus.Configure.With()
                             .DefaultBuilder()
                             .DefiningCommandsAs(t => t.Namespace != null && t.Namespace.EndsWith("Commands"))
                             .DefiningEventsAs(t => t.Namespace != null && t.Namespace.EndsWith("Events"))
                             .JsonSerializer()
                             .MsmqTransport()
                             .IsTransactional(false)
                             .PurgeOnStartup(false)
                             .UnicastBus()
                             .ImpersonateSender(false)
                             .CreateBus()
                             .Start(
                                 () =>
                                 Configure.Instance.ForInstallationOn<NServiceBus.Installation.Environments.Windows>()
                                          .Install());
        }

        public static IBus Bus { get; set; }
    }
}
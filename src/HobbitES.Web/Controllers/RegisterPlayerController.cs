using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using HobbitES.Messages.Commands;

namespace HobbitES.Web.Controllers
{
    public class RegisterPlayerController : Controller
    {
        //
        // GET: /RegisterPlayer/

        public ActionResult Index()
        {
            return View();
        }

        public ViewResult Register(string playerId)
        {
            MvcApplication.Bus.Send(new RegisterPlayer(Guid.NewGuid(), playerId));
            return View();
        }
    }
}

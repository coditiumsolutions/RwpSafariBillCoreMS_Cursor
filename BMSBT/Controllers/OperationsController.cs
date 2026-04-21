using Microsoft.AspNetCore.Mvc;

namespace BMSBT.Controllers
{
    public class OperationsController : Controller
    {
        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            return View();
        }
    }
}

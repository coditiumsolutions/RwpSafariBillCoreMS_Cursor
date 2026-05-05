using BMSBT.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Diagnostics;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    public class UsersManagementController : Controller
    {
        private readonly ILogger<UsersManagementController> _logger;
        private readonly BmsbtContext context;
        private readonly PasswordHasher<User> _passwordHasher;
        public UsersManagementController(ILogger<UsersManagementController> logger, BmsbtContext context)
        {
            _logger = logger;
            this.context = context;
            _passwordHasher = new PasswordHasher<User>();
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var sessionUser = HttpContext.Session.GetString("UserName");
            if (string.IsNullOrWhiteSpace(sessionUser))
            {
                context.Result = RedirectToAction("Index", "Login");
                return;
            }

            if (!HasUserSetupRole())
            {
                TempData["AccessDeniedMessage"] = "you do no have rights to open the link";
                context.Result = RedirectToAction("AccessDenied", "Login");
                return;
            }

            base.OnActionExecuting(context);
        }



        //[Authorize]
        public IActionResult Index(int? page)
        {
            int pageSize = 10; // Number of records per page
            int pageNumber = page ?? 1; // Default to page 1 if no page is specified

            var data = context.Users.ToList().ToPagedList(pageNumber, pageSize);
            return View(data);
        }



        //[HttpGet]
        //[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        //public IActionResult Index()
        //{
        //    return View();
        //}

        public IActionResult Home()
        {
            var data = context.Users.ToList();
            return View(data);
        }

        public IActionResult Users(int? page)
        {
            int pageSize = 10; // Number of records per page
            int pageNumber = page ?? 1; // Default to page 1 if no page is specified

            var data = context.Users.ToList().ToPagedList(pageNumber, pageSize);
            return View(data);
        }


        public IActionResult Customers()
        {
            var data = context.CustomersDetails.ToList();
            return View(data);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }












        [HttpGet]
        public IActionResult CreateUser()
        {
            ViewBag.AvailableRoles = GetConfiguredRoles();
            return View();
        }




        [HttpPost]
        public IActionResult CreateUser(User user, List<string> Role)
        {
            if (Role != null && Role.Count > 0)
            {
                user.Role = string.Join(",", Role); // Store roles as comma-separated string
            }

            // Hash the password before saving
            user.PasswordHash = _passwordHasher.HashPassword(user, user.PasswordHash);

            context.Users.Add(user);
            context.SaveChanges();

            return RedirectToAction("Index");
        }


        [HttpGet]
        public IActionResult EditUser(int id)
        {
            var user = context.Users.Find(id);
            if (user == null)
            {
                return NotFound();
            }

            // If Role is not null, convert it into a list for multi-selection
            ViewBag.SelectedRoles = user.Role?.Split(',') ?? new string[] { };
            ViewBag.AvailableRoles = GetConfiguredRoles();

            return View(user);
        }

        [HttpPost]

        public IActionResult EditUser(User user, string[] Role, string? newPassword)
        {
            var existingUser = context.Users.FirstOrDefault(u => u.Uid == user.Uid);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.EmployeeId = user.EmployeeId;
            existingUser.Username = user.Username;
            existingUser.Role = Role != null ? string.Join(",", Role) : null;

            // Hash new password only if provided
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                existingUser.PasswordHash = _passwordHasher.HashPassword(existingUser, user.PasswordHash);
            }

            context.SaveChanges();
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int id)
        {
            var sessionUser = HttpContext.Session.GetString("UserName");
            var user = context.Users.FirstOrDefault(u => u.Uid == id);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            if (!string.IsNullOrEmpty(sessionUser) &&
                string.Equals(user.Username, sessionUser, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "You cannot delete your own account while signed in.";
                return RedirectToAction("Index");
            }

            context.Users.Remove(user);
            context.SaveChanges();
            TempData["Message"] = "User deleted successfully.";
            return RedirectToAction("Index");
        }

        private List<string> GetConfiguredRoles()
        {
            return context.Configurations
                .Where(c => c.ConfigKey != null
                            && c.ConfigKey.Trim().ToLower() == "roles"
                            && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
        }

        private bool HasUserSetupRole()
        {
            var rolesText = HttpContext.Session.GetString("Role");
            if (string.IsNullOrWhiteSpace(rolesText))
                return false;

            return rolesText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Any(r => string.Equals(r, "UserSetup", StringComparison.OrdinalIgnoreCase));
        }
    }
}

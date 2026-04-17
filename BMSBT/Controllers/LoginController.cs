using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BMSBT.Models;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using BMSBT.BillServices;
using System.Text.Json;

namespace BMSBT.Controllers
{
    public class LoginController : Controller
    {
        private readonly BmsbtContext _context;
        private readonly ICurrentOperatorService _operatorService;
        public LoginController(BmsbtContext context, ICurrentOperatorService operatorService)
        {
            _context = context;
            _operatorService = operatorService;
            _operatorService = operatorService;
        }
        MaintenanceBill m = new MaintenanceBill();


        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string username, string password)  // Make the method async
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Username and Password are required.";
                return View();
            }

            // Find user by username
            var user = _context.Users.FirstOrDefault(u => u.Username == username);

            if (user == null)
            {
                ViewBag.Error = "Invalid username or password.";
                return View();
            }

            if (user.PasswordHash == password)
            {
                // ✅ Await the InitializeAsync call
              
                // Create session
                HttpContext.Session.SetString("UserName", user.Username);
                HttpContext.Session.SetString("Role", user.Role ?? "");
                HttpContext.Session.SetString("LoginTime", DateTime.Now.ToString("hh:mm tt"));

                // Fetch operator setup using OperatorName matched with UserName
                var operatorSetup = _context.OperatorsSetups
                    .FirstOrDefault(o => o.OperatorName == user.Username);

                // Billing screens use OperatorsSetup.OperatorID with ICurrentOperatorService — prefer it when present
                var sessionOperatorId = user.EmployeeId ?? "";
                if (operatorSetup != null && !string.IsNullOrWhiteSpace(operatorSetup.OperatorID))
                    sessionOperatorId = operatorSetup.OperatorID.Trim();
                else if (string.IsNullOrWhiteSpace(sessionOperatorId) && operatorSetup != null)
                    sessionOperatorId = operatorSetup.OperatorID?.Trim() ?? "";

                HttpContext.Session.SetString("OperatorId", sessionOperatorId);

                if (operatorSetup != null)
                {
                    var operatorSetupDetail = new Dictionary<string, string>
    {
        { "OperatorId", operatorSetup.OperatorID ?? "" },
        { "OperatorName", operatorSetup.OperatorName ?? "" },
        { "BillingMonth", operatorSetup.BillingMonth ?? "" },
        { "BillingYear", operatorSetup.BillingYear ?? "" }
    };

                    HttpContext.Session.SetString("OperatorSetupDetail", JsonSerializer.Serialize(operatorSetupDetail));
                }



                // Get operator details by OperatorId from session
                var operatorId = HttpContext.Session.GetString("OperatorId");

                if (!string.IsNullOrEmpty(operatorId))
                {
                    var operatorDetails = _context.OperatorsSetups
                        .FirstOrDefault(o => o.OperatorID == operatorId);

                    if (operatorDetails != null)
                    {
                        BillCreationState.CurrentMonth = operatorDetails.BillingMonth ?? "";
                        BillCreationState.CurrentYear = operatorDetails.BillingYear ?? "";
                    }
                }




                var claims = new List<Claim>
                 {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                 };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);




                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Invalid username or password.";
            return View();
        }




        public IActionResult AccessDenied()
        {
            return View();
        }


        public IActionResult Logout()
        {
            //await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            //// Clear all cookies explicitly
            //foreach (var cookie in Request.Cookies.Keys)
            //{
            //    Response.Cookies.Delete(cookie);
            //}

            HttpContext.Session.Clear();
            return RedirectToAction("Index");

        }


        private string HashPassword(string password)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }



        //2. Important Login Detail
        //Login Method which uses Cookies Detail
        [Authorize(Roles = "Admin")]
        public IActionResult AdminDashboard()
        {
            return View();
        }



        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            //  if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            if (user != null && user.PasswordHash == password)
            {
                var operatorSetup = await _context.OperatorsSetups
                    .FirstOrDefaultAsync(o => o.OperatorName == user.Username);

                var sessionOperatorId = user.EmployeeId ?? "";
                if (operatorSetup != null && !string.IsNullOrWhiteSpace(operatorSetup.OperatorID))
                    sessionOperatorId = operatorSetup.OperatorID.Trim();
                else if (string.IsNullOrWhiteSpace(sessionOperatorId) && operatorSetup != null)
                    sessionOperatorId = operatorSetup.OperatorID?.Trim() ?? "";

                // Create session
                HttpContext.Session.SetString("UserName", user.Username);
                HttpContext.Session.SetString("Role", user.Role ?? "");
                HttpContext.Session.SetString("OperatorId", sessionOperatorId);
                HttpContext.Session.SetString("LoginTime", DateTime.Now.ToString("hh:mm tt"));

                if (operatorSetup != null)
                {
                    var operatorSetupDetail = new Dictionary<string, string>
                    {
                        { "OperatorId", operatorSetup.OperatorID ?? "" },
                        { "OperatorName", operatorSetup.OperatorName ?? "" },
                        { "BillingMonth", operatorSetup.BillingMonth ?? "" },
                        { "BillingYear", operatorSetup.BillingYear ?? "" }
                    };
                    HttpContext.Session.SetString("OperatorSetupDetail", JsonSerializer.Serialize(operatorSetupDetail));
                }

                var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role ?? "User")
            };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                // Await SignInAsync for asynchronous operation
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));


                return RedirectToAction("Index", "Home");
            }

            ViewBag.Error = "Invalid username or password";
            return View();
        }





    }
}

using BMSBT.Models;
using BMSBT.Roles;
using BMSBT.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
namespace BMSBT.Controllers
{
   
    public class SetupController : Controller
    {
        private readonly BmsbtContext db;
        private readonly PasswordHasher<User> _passwordHasher;

        public SetupController(BmsbtContext context)
        {
            db = context;
            _passwordHasher = new PasswordHasher<User>();
        }

        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }
            ViewBag.Username = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            return View();
        }

        public IActionResult AllTarrifs()
        {
            var data = db.Tarrifs.ToList();
            return View(data);
        }

        [HttpGet]
        public IActionResult Profile()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Index", "Login");
            }

            PopulateLayoutSessionInfo();
            var username = HttpContext.Session.GetString("UserName");
            var user = db.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Unable to load profile.";
                return RedirectToAction("Index");
            }

            var model = new ProfileViewModel
            {
                UserId = user.Uid,
                Username = user.Username,
                EmployeeId = user.EmployeeId,
                Role = user.Role,
                LoginTime = HttpContext.Session.GetString("LoginTime")
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult UpdateProfile()
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Index", "Login");
            }

            PopulateLayoutSessionInfo();
            return View(new UpdateProfileViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProfile(UpdateProfileViewModel model)
        {
            if (!EnsureLoggedIn())
            {
                return RedirectToAction("Index", "Login");
            }

            PopulateLayoutSessionInfo();
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (!string.Equals(model.NewPassword, model.ConfirmPassword, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(UpdateProfileViewModel.ConfirmPassword), "Confirm Password does not match New Password.");
                return View(model);
            }

            var username = HttpContext.Session.GetString("UserName");
            var user = db.Users.FirstOrDefault(u => u.Username == username);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "User profile not found.");
                return View(model);
            }

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, model.CurrentPassword);
            if (verifyResult == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(nameof(UpdateProfileViewModel.CurrentPassword), "Current password is incorrect.");
                return View(model);
            }

            user.PasswordHash = _passwordHasher.HashPassword(user, model.NewPassword);
            db.SaveChanges();
            TempData["SuccessMessage"] = "Password updated successfully.";
            return RedirectToAction("Profile");
        }

        private bool EnsureLoggedIn()
        {
            return HttpContext.Session.GetString("UserName") != null;
        }

        private void PopulateLayoutSessionInfo()
        {
            ViewBag.Username = HttpContext.Session.GetString("UserName");
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        }

    }
}

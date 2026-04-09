using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using BMSBT.Models;
using BMSBT.BillServices;
using BMSBT.Models.MyObjects;
using BMSBT.EBillService;
using BMSBT.Services;
using BMSBT.Helper;

namespace BMSBT;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHttpClient("NoSslValidation")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        builder.Services.AddHttpClient();

        // Add services to the container - keep only one instance of each
        builder.Services.AddHttpContextAccessor(); // Only one instance needed
        builder.Services.AddMemoryCache();
        builder.Services.AddControllersWithViews(); // Only one instance needed

        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        // Database context
        builder.Services.AddDbContext<BmsbtContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Authentication
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Login/Index";
                options.AccessDeniedPath = "/Login/AccessDenied";
            });

        // Register your services
        builder.Services.AddScoped<ICurrentOperatorService, CurrentOperatorService>();
        builder.Services.AddScoped<IOperatorService, OperatorService>();  // Register the OperatorService
        builder.Services.AddScoped<SessionHelper>();

        // Maintenance bills insert service (isolated from MaintenanceNew UI)
        builder.Services.AddScoped<IMaintenanceBillInsertService, MaintenanceBillInsertService>();
        builder.Services.AddScoped<IBillingService, BillingService>();

        builder.Services.AddScoped<IOperatorSettingService, OperatorSettingService>();  // Operator settings

        var app = builder.Build();

        // Configure the HTTP request pipeline
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        // Cache control middleware
        app.Use(async (context, next) =>
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
            await next();
        });

        app.UseSession();

        // Middleware pipeline
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Login}/{action=Index}/{id?}");

        app.Run();
    }
}
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Auth;

public static class StaffAuthentication
{
    public static readonly string[] Roles = ["Admin", "Manager", "Operator"];

    public static void AddStaffAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<PharmacyDbContext>().AddDefaultTokenProviders();
        builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = PharmacyPos.Api.Demo.DemoMode.Enabled(builder.Environment) ? "PharmacyPos.Demo.Session" : "PharmacyPos.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = (builder.Environment.IsDevelopment() || PharmacyPos.Api.Demo.DemoMode.Enabled(builder.Environment))
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context => {
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (id == null) return;
                var db = context.HttpContext.RequestServices.GetRequiredService<PharmacyDbContext>();
                if (await db.StaffAccounts.AnyAsync(a => a.UserId == id && a.Disabled)) {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                }
            };
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("Staff", policy => policy.RequireAuthenticatedUser().RequireRole(Roles))
            .AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = PharmacyPos.Api.Demo.DemoMode.Enabled(builder.Environment) ? "PharmacyPos.Demo.Antiforgery" : "PharmacyPos.Antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = (builder.Environment.IsDevelopment() || PharmacyPos.Api.Demo.DemoMode.Enabled(builder.Environment))
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("login", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));
        });
    }

    public static void MapStaffAuthentication(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapGet("/session", async (HttpContext context, IAntiforgery antiforgery,
            UserManager<IdentityUser> users) =>
        {
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers.Pragma = "no-cache";
            var user = await users.GetUserAsync(context.User);
            var token = antiforgery.GetAndStoreTokens(context).RequestToken;
            return Results.Ok(new
            {
                csrfToken = token,
                demo = PharmacyPos.Api.Demo.DemoMode.Enabled(app.Environment),
                user = user is null ? null : new { username = user.UserName, roles = await users.GetRolesAsync(user) }
            });
        }).AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, HttpContext context,
            IAntiforgery antiforgery, SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, PharmacyDbContext db) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await HasValidToken(context, antiforgery)) return Results.BadRequest();
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 256
                || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.BadRequest(new { message = "Enter a valid username and password." });
            var user = await users.FindByNameAsync(request.Username.Trim());
            if (user != null && await db.StaffAccounts.AnyAsync(a => a.UserId == user.Id && a.Disabled))
                return Results.Json(new { message = "Unable to sign in. Check your details or try again later." }, statusCode: 401);
            var result = await signIn.PasswordSignInAsync(request.Username.Trim(), request.Password,
                isPersistent: false, lockoutOnFailure: true);
            // The same response covers missing users, bad passwords, and locked accounts.
            return result.Succeeded ? Results.NoContent() : Results.Json(
                new { message = "Unable to sign in. Check your details or try again later." }, statusCode: 401);
        }).AllowAnonymous().RequireRateLimiting("login");

        group.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery,
            SignInManager<IdentityUser> signIn) =>
        {
            if (!await HasValidToken(context, antiforgery)) return Results.BadRequest();
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<bool> HasValidToken(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }

    public sealed record LoginRequest(string Username, string Password);
}

using System.Security.Claims;
using System.Text;
using LibraryLending.Application;
using LibraryLending.Infrastructure.Persistence;
using LibraryLending.Infrastructure.Seeding;
using LibraryLending.Infrastructure.Identity;
using LibraryLending.Web;
using LibraryLending.Web.Components;
using LibraryLending.Web.State;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Security.Cryptography.X509Certificates;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var connection = builder.Configuration.GetConnectionString("Library") ?? throw new InvalidOperationException("Library connection string is required.");
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection).DataSource;
if (dataSource != ":memory:" && !string.IsNullOrEmpty(Path.GetDirectoryName(dataSource))) Directory.CreateDirectory(Path.GetDirectoryName(dataSource)!);
var options = builder.Configuration.GetSection("Library").Get<LibraryOptions>() ?? new();
builder.Services.AddSingleton(options); builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<LibraryDbContext>(o => o.UseSqlite(connection));
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database");
builder.Services.AddHttpsRedirection(o => o.HttpsPort = builder.Configuration.GetValue<int?>("Hosting:HttpsPort") ?? 443);
var trustedProxies = builder.Configuration.GetSection("Hosting:KnownProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear(); o.KnownProxies.Clear();
        foreach (var address in trustedProxies) o.KnownProxies.Add(IPAddress.Parse(address));
    });
if (!builder.Environment.IsDevelopment())
{
    var keyDirectory = builder.Configuration["DataProtection:KeyPath"];
    if (string.IsNullOrWhiteSpace(keyDirectory)) throw new InvalidOperationException("Production requires DataProtection:KeyPath on persistent restricted storage.");
    var protection = builder.Services.AddDataProtection().SetApplicationName("BookBridge")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(keyDirectory, builder.Environment.ContentRootPath)));
    if (builder.Configuration["DataProtection:CertificatePath"] is { Length: > 0 } certificatePath)
        protection.ProtectKeysWithCertificate(new X509Certificate2(certificatePath, builder.Configuration["DataProtection:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet));
}
builder.Services.AddIdentity<IdentityUser, IdentityRole>(o => { o.Password.RequiredLength = 10; o.User.RequireUniqueEmail = true; o.Lockout.MaxFailedAccessAttempts = 5; })
 .AddEntityFrameworkStores<LibraryDbContext>().AddDefaultTokenProviders().AddClaimsPrincipalFactory<LibraryClaimsFactory>();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login"; o.AccessDeniedPath = "/account/login";
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
builder.Services.AddAuthorization(); builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents(); builder.Services.AddSignalR();
builder.Services.AddScoped<IdentitySessionValidator>();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityStateProvider>();
builder.Services.AddSingleton<LibraryWriteCoordinator>();
builder.Services.AddScoped<ILibraryRepository, EfLibraryRepository>(); builder.Services.AddScoped<ILibraryService, LibraryService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IMemberAccountService, MemberAccountService>();
builder.Services.AddSingleton<INotificationPublisher, SignalRNotificationPublisher>();
builder.Services.AddScoped<AppState>(); builder.Services.AddScoped<CurrentActor>();
if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true)) builder.Services.AddHostedService<DeadlineWorker>();
var app = builder.Build();
if (trustedProxies.Length > 0) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseExceptionHandler(error => error.Run(async context => { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please retry or contact the librarian." }); }));
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    if (app.Configuration.GetValue("Database:ApplyMigrations", app.Environment.IsDevelopment())) await db.Database.MigrateAsync();
    else if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Apply pending database migrations before starting production.");
    await IdentityBootstrapper.InitializeAsync(scope.ServiceProvider, app.Configuration);
    // The explicit Demo environment is for disposable public samples. Production never seeds demo accounts.
    if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demo")) && app.Configuration.GetValue("Demo:Seed", false)) await DemoSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>(), scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), options);
}
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapHealthChecks("/health/ready");
app.MapPost("/account/sign-in", async (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) =>
{
    try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form token. Reload the login page."); }
    var form = await context.Request.ReadFormAsync(); var email = form["email"].ToString().Trim(); var password = form["password"].ToString();
    var result = await signIn.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
    if (!result.Succeeded) return Results.LocalRedirect("/account/login?error=" + Uri.EscapeDataString(result.IsLockedOut ? "Too many attempts. Please try again later." : "Email or password is incorrect."));
    var target = form["returnUrl"].ToString(); if (string.IsNullOrEmpty(target) || !target.StartsWith('/') || target.StartsWith("//") || target.StartsWith("/\\")) target = "/";
    return Results.LocalRedirect(target);
}).AllowAnonymous();
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) => { try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form token."); } await signIn.SignOutAsync(); return Results.LocalRedirect("/account/login"); }).RequireAuthorization();
app.MapPost("/account/change-password", async (HttpContext context, IAntiforgery antiforgery, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    try { await antiforgery.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest("Invalid form token. Reload the page."); }
    var form = await context.Request.ReadFormAsync(); var current = form["currentPassword"].ToString(); var next = form["newPassword"].ToString();
    if (next != form["confirmPassword"].ToString()) return Results.LocalRedirect("/account/password?error=" + Uri.EscapeDataString("New passwords do not match."));
    var user = await users.GetUserAsync(context.User); if (user is null) return Results.Unauthorized();
    var result = await users.ChangePasswordAsync(user, current, next);
    if (!result.Succeeded) return Results.LocalRedirect("/account/password?error=" + Uri.EscapeDataString(string.Join(" ", result.Errors.Select(x => x.Description))));
    await signIn.RefreshSignInAsync(user); return Results.LocalRedirect("/account/password?success=Password%20updated.");
}).RequireAuthorization();
app.MapGet("/exports/overdue.csv", async (HttpContext context, ILibraryService service, LibraryOptions settings) =>
{
    var actor = new Actor(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, context.User.IsInRole("Librarian"), null);
    var rows = await service.GetLoansAsync(actor, true, context.Request.Query["search"].ToString(), context.Request.Query["sort"].ToString(), context.RequestAborted);
    static string Cell(string value) { if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
    var csv = new StringBuilder("Member,Book,Barcode,Due date,Days overdue," + Cell($"Fine ({settings.CurrencyLabel})") + "\r\n");
    foreach (var r in rows) csv.AppendLine(string.Join(",", Cell(r.MemberName), Cell(r.BookTitle), Cell(r.Barcode), r.DueDate.ToString("yyyy-MM-dd"), r.DaysOverdue.ToString(), (r.FineMinor / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", "overdue-loans.csv");
}).RequireAuthorization(policy => policy.RequireRole("Librarian"));
app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using PzServerManager.Application.Servers;
using PzServerManager.Infrastructure;
using PzServerManager.Infrastructure.Authentication;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Web.Components;
using PzServerManager.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var listenUrl = builder.Configuration[$"{ManagerOptions.SectionName}:ListenUrl"];
if (!string.IsNullOrWhiteSpace(listenUrl))
{
    builder.WebHost.UseUrls(listenUrl);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>(
            $"{ManagerOptions.SectionName}:RequireHttpsCookie")
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddPzManagerInfrastructure(builder.Configuration);
builder.Services.AddScoped<ServerInstanceService>();
builder.Services.AddScoped<ServerInstallationService>();
if (builder.Configuration.GetSection($"{ManagerOptions.SectionName}:ManagedServer").Exists())
{
    builder.Services.AddHostedService<AutomationBackgroundService>();
    builder.Services.AddHostedService<CardinalBackgroundService>();
}

var app = builder.Build();

if (args.Length > 0 && args[0].Equals("--set-password", StringComparison.Ordinal))
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Uso: PzServerManager.Web --set-password <usuario>");
        Environment.ExitCode = 2;
        return;
    }

    var password = ReadPassword("Senha: ");
    if (!Console.IsInputRedirected)
    {
        var confirmation = ReadPassword("Repita a senha: ");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("As senhas não coincidem.");
            Environment.ExitCode = 2;
            return;
        }
    }

    try
    {
        await app.Services.GetRequiredService<UserFileStore>().SetPasswordAsync(args[1], password);
        Console.WriteLine($"Senha do usuário '{args[1].Trim()}' atualizada.");
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        Environment.ExitCode = 2;
    }

    return;
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IInfrastructureInitializer>();
    await initializer.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Text("ok", "text/plain"))
    .AllowAnonymous();

app.MapPost("/auth/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserFileStore users,
    LoginAttemptLimiter limiter) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var userName = form["usuario"].ToString();
    var password = form["senha"].ToString();
    var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    if (limiter.IsBlocked(userName, ipAddress))
    {
        return Results.Redirect("/login?erro=1");
    }

    if (!await users.VerifyAsync(userName, password, context.RequestAborted))
    {
        limiter.RecordFailure(userName, ipAddress);
        return Results.Redirect("/login?erro=1");
    }

    limiter.RecordSuccess(userName, ipAddress);
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, userName.Trim())],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });

    var returnUrl = form["returnUrl"].ToString();
    return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl : "/");
}).AllowAnonymous();

app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.Run();

static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    Console.Write(prompt);
    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string(characters.ToArray());
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            characters.Add(key.KeyChar);
        }
    }
}

static bool IsLocalReturnUrl(string returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl.StartsWith('/') &&
    !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
    !returnUrl.StartsWith("/\\", StringComparison.Ordinal);

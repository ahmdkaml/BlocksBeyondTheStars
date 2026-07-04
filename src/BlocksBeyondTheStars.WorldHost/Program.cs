// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;
using Microsoft.AspNetCore.HttpOverrides;

// Hosted-worlds control plane ("WorldHost"): accounts, world registry, wake-on-demand allocation and
// join-token issuing for a fleet of one-container-per-world dedicated servers. See
// docs/developer/HOSTED_WORLDS.md for the architecture (routing, DNS, certificates, lifecycle).
//
// Deliberately NOT part of the per-instance admin Api: this service owns MANY worlds and the Docker
// socket; the Api serves ONE installation. Bound to loopback by default — the public portal domain is
// proxied onto it by Caddy.

var config = WorldHostConfig.FromEnvironment();
var registry = new HostRegistry(config);
IInstanceLauncher launcher = new DockerCliLauncher(config);
var metrics = new WorldHostMetrics();
var orchestrator = new WorldOrchestrator(config, registry, launcher, metrics: metrics);

// Abuse limits (Phase 3). Signup/login key on the caller IP (real one via X-Forwarded-For — Caddy
// fronts this service), uploads/reports on the account. See WorldHostConfig for the operator knobs.
var signupLimit = new RateLimiter(config.SignupPerHourPerIp, TimeSpan.FromHours(1));
var loginLimit = new RateLimiter(config.LoginPerMinutePerIp, TimeSpan.FromMinutes(1));
var uploadLimit = new RateLimiter(config.UploadsPerHourPerAccount, TimeSpan.FromHours(1));
var reportLimit = new RateLimiter(config.ReportsPerHourPerAccount, TimeSpan.FromHours(1));

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");

// Honor X-Forwarded-* from the fronting Caddy so rate limits key on the real client IP, not the proxy.
// The proxy is a trusted sibling container on an arbitrary IP, so clear the loopback-only allow-list.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
var log = app.Logger;

string CallerIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

IResult RateLimited()
{
    metrics.RateLimited();
    return Results.Json(new { error = "Too many requests — please wait a bit and try again." },
        statusCode: StatusCodes.Status429TooManyRequests);
}

// Resolves the caller's account from the Authorization: Bearer <session> header; null = not signed in.
AccountRecord? Caller(HttpContext ctx)
{
    string header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? registry.ResolveSession(header.Substring(prefix.Length).Trim())
        : null;
}

// Strips CR/LF so a player-supplied string (name, reason) can never forge extra log lines. Account and
// world names are charset-validated anyway; this covers free-text fields and satisfies defense in depth.
static string LogSafe(string? value)
    => (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

// Uniform account-state gate for world actions: banned accounts and accounts that haven't accepted the
// CURRENT rules version are refused (the join path re-checks inside the orchestrator as well — that is
// the choke point native clients will use directly).
IResult? GuardAccount(AccountRecord account)
{
    if (account.IsBanned)
    {
        return Results.Json(new
        {
            error = string.IsNullOrEmpty(account.BanReason) ? "This account is banned." : $"This account is banned: {account.BanReason}",
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (account.AcceptedTermsVersion < config.TermsVersion)
    {
        return Results.Json(new
        {
            error = "The community rules have changed — please accept them first.",
            termsOutdated = true,
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    return null;
}

bool IsAdmin(HttpContext ctx)
    => !string.IsNullOrEmpty(config.AdminToken)
       && string.Equals(ctx.Request.Headers["X-Admin-Token"].ToString(), config.AdminToken, StringComparison.Ordinal);

app.MapGet("/healthz", () => Results.Text("ok\n"));

// Prometheus scrape (Phase 3). Reachable only on the loopback bind — Caddy deliberately does not
// route /metrics, so fleet numbers never leak publicly.
app.MapGet("/metrics", () => Results.Text(metrics.Render(registry), "text/plain; version=0.0.4; charset=utf-8"));

// ---------------- Portal pages (server-rendered shells; the JS talks to /api with a Bearer session) ----------------

app.MapGet("/", () => Results.Content(WorldHostPortalPages.Landing(config), "text/html; charset=utf-8"));
app.MapGet("/worlds", () => Results.Content(WorldHostPortalPages.Worlds(config), "text/html; charset=utf-8"));
app.MapGet("/rules", () => Results.Content(WorldHostPortalPages.Rules(config), "text/html; charset=utf-8"));

// Caddy on-demand TLS gate: before issuing a certificate for a requested hostname, Caddy asks this
// endpoint. 200 only for the portal host itself and subdomains of real worlds — so nobody can make us
// mint certificates (and burn rate limits) for arbitrary names pointed at our IP.
app.MapGet("/ask", (string? domain) =>
{
    if (string.IsNullOrEmpty(domain))
    {
        return Results.NotFound();
    }

    if (string.Equals(domain, config.BaseDomain, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok();
    }

    string suffix = "." + config.BaseDomain;
    if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && registry.FindBySubdomain(domain.Substring(0, domain.Length - suffix.Length).ToLowerInvariant()) != null)
    {
        return Results.Ok();
    }

    return Results.NotFound();
});

// ---------------- Accounts ----------------

app.MapPost("/api/signup", (HttpContext ctx, SignupRequest req) =>
{
    if (!signupLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    var (ok, error, accountId, session) = registry.CreateAccount(req.Name, req.Password, req.ClaimCode, req.AcceptedTermsVersion);
    if (!ok)
    {
        return Results.BadRequest(new { error });
    }

    // Deliberately no account id in the log: ids act as stable references in the registry and appearing
    // in log files would let anyone with log access correlate them (CodeQL cs/cleartext-storage).
    log.LogInformation("Account created: {Name}.", LogSafe(req.Name));
    return Results.Json(new { accountId, sessionToken = session });
});

app.MapPost("/api/login", (HttpContext ctx, SignupRequest req) =>
{
    if (!loginLimit.TryPass(CallerIp(ctx)))
    {
        return RateLimited();
    }

    if (registry.Login(req.Name, req.Password) is not { } login)
    {
        return Results.Unauthorized();
    }

    // termsOutdated tells the portal/client to show the re-acceptance screen before world actions.
    var account = registry.ResolveSession(login.SessionToken)!;
    return Results.Json(new
    {
        accountId = login.AccountId,
        sessionToken = login.SessionToken,
        termsOutdated = account.AcceptedTermsVersion < config.TermsVersion,
    });
});

app.MapPost("/api/accept-terms", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    registry.AcceptTerms(account.Id, config.TermsVersion);
    return Results.Ok();
});

// ---------------- Player reports ("Spieler melden") ----------------

app.MapPost("/api/reports", (HttpContext ctx, ReportRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!reportLimit.TryPass(account.Id))
    {
        return RateLimited();
    }

    // Banned players may still file reports (they can't play, but silencing them buys nothing);
    // reports are length-capped and reviewed manually — nobody is auto-punished by a report.
    var (ok, error) = registry.CreateReport(account.Id, req.WorldId ?? string.Empty, req.ReportedName, req.Category, req.Message ?? string.Empty);
    return ok ? Results.Ok() : Results.BadRequest(new { error });
});

// ---------------- Operator admin (X-Admin-Token; disabled when no token is configured) ----------------

app.MapGet("/api/admin/reports", (HttpContext ctx) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    return Results.Json(new { reports = registry.ListOpenReports() });
});

app.MapPost("/api/admin/reports/{id:long}/close", (HttpContext ctx, long id, CloseReportRequest req) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    string status = req.Status is "dismissed" ? "dismissed" : "reviewed";
    registry.CloseReport(id, status);
    return Results.Ok();
});

app.MapPost("/api/admin/ban", (HttpContext ctx, BanRequest req) =>
{
    if (!IsAdmin(ctx))
    {
        return Results.Unauthorized();
    }

    registry.SetBanned(req.AccountId, req.Banned, req.Reason ?? string.Empty);
    log.LogInformation("Account {Id} {Action} ({Reason}).", LogSafe(req.AccountId), req.Banned ? "BANNED" : "unbanned", LogSafe(req.Reason));
    return Results.Ok();
});

// ---------------- Worlds ----------------

app.MapGet("/api/worlds", (HttpContext ctx) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    var worlds = registry.ListWorlds(account.Id)
        .Select(w => new { id = w.Id, name = w.DisplayName, status = w.Status, subdomain = w.Subdomain });
    return Results.Json(new { worlds });
});

app.MapPost("/api/worlds", (HttpContext ctx, CreateWorldRequest req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (GuardAccount(account) is { } blocked)
    {
        return blocked;
    }

    var (ok, error, world) = registry.CreateWorld(account.Id, req.Name);
    if (!ok)
    {
        return Results.BadRequest(new { error });
    }

    log.LogInformation("World '{Name}' ({Id}) created by {Account}.", LogSafe(world!.DisplayName), world.Id, LogSafe(account.Name));
    return Results.Json(new { id = world.Id, name = world.DisplayName, status = world.Status, subdomain = world.Subdomain });
});

app.MapPost("/api/worlds/{id}/join", async (HttpContext ctx, string id, JoinRequestDto req) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is null)
    {
        return Results.NotFound();
    }

    // Any signed-in account may request a join grant — access control happens at the game layer: the
    // instance only admits valid tokens, and invite/visibility rules come with Phase 2. The grant names
    // the caller's account, so the instance can attribute every admitted player.
    var (grant, error) = await orchestrator.JoinAsync(id, account, req.PlayerName);
    if (grant is null)
    {
        return Results.Json(new { error }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Json(grant);
});

app.MapPost("/api/worlds/{id}/stop", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    orchestrator.StopWorld(world);
    return Results.Ok();
});

app.MapDelete("/api/worlds/{id}", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    // Stop first so no orphan container keeps running under a deleted registry row. The saves directory
    // is intentionally NOT removed — an operator can still recover/export it; automated retention is Phase 3.
    orchestrator.StopWorld(world);
    registry.DeleteWorld(world.Id);
    log.LogInformation("World '{Name}' ({Id}) deleted by {Account} (saves directory retained).", LogSafe(world.DisplayName), world.Id, LogSafe(account.Name));
    return Results.Ok();
});

// ---------------- Save upload / export (the SP↔hosted round-trip) ----------------

app.MapPost("/api/worlds/{id}/save", async (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (GuardAccount(account) is { } blocked)
    {
        return blocked;
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    if (!uploadLimit.TryPass(account.Id))
    {
        return RateLimited();
    }

    // Only while stopped: the instance owns the file when it runs, and a mid-write copy would corrupt.
    if (registry.GetWorld(id)!.Status != WorldStatus.Stopped || launcher.IsRunning(world.ContainerId))
    {
        return Results.BadRequest(new { error = "Stop the world before uploading a save." });
    }

    // Stream to a temp file with a hard size cap, then validate BEFORE it replaces anything.
    string tmp = Path.Combine(Path.GetTempPath(), $"bbs-upload-{world.Id}-{Guid.NewGuid():N}.db");
    try
    {
        await using (var file = File.Create(tmp))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > config.UploadMaxBytes)
                {
                    return Results.BadRequest(new { error = $"Save exceeds the {config.UploadMaxBytes / (1024 * 1024)} MB upload limit." });
                }

                await file.WriteAsync(buffer.AsMemory(0, read));
            }

            if (total == 0)
            {
                return Results.BadRequest(new { error = "Empty upload." });
            }
        }

        var (ok, error) = SavePaths.ValidateUploadedSave(tmp);
        if (!ok)
        {
            return Results.BadRequest(new { error });
        }

        // Keep exactly one previous generation as a manual-recovery net, then move the upload into place.
        string target = SavePaths.WorldDbPath(config, world.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            File.Copy(target, target + ".bak", overwrite: true);
        }

        File.Move(tmp, target, overwrite: true);
        log.LogInformation("World '{Name}' ({Id}): save uploaded by {Account}.", LogSafe(world.DisplayName), world.Id, LogSafe(account.Name));
        return Results.Ok();
    }
    finally
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
    }
});

app.MapGet("/api/worlds/{id}/save", (HttpContext ctx, string id) =>
{
    if (Caller(ctx) is not { } account)
    {
        return Results.Unauthorized();
    }

    if (!HostRegistry.IsValidWorldId(id) || registry.GetWorld(id) is not { } world)
    {
        return Results.NotFound();
    }

    if (world.OwnerAccountId != account.Id)
    {
        return Results.Forbid();
    }

    if (registry.GetWorld(id)!.Status != WorldStatus.Stopped || launcher.IsRunning(world.ContainerId))
    {
        return Results.BadRequest(new { error = "Stop the world before downloading its save." });
    }

    string path = SavePaths.WorldDbPath(config, world.Id);
    if (!File.Exists(path))
    {
        return Results.BadRequest(new { error = "This world has no save yet (it was never started)." });
    }

    return Results.File(path, "application/octet-stream", $"{world.Id}-world.db");
});

// ---------------- Background reaper ----------------

// Reconcile registry vs Docker every 30 s: instances that exited themselves (idle shutdown — the normal
// sleep path) get marked stopped so the next join wakes them and world lists stay truthful.
var reaper = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    int ticks = 0;
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            int reaped = orchestrator.Reap();
            if (reaped > 0)
            {
                log.LogInformation("Reaper: {Count} idle-stopped world(s) marked stopped.", reaped);
            }

            // Archive sweep once an hour (120 × 30 s): long-inactive stopped worlds move to the archive.
            if (++ticks % 120 == 0)
            {
                int archived = orchestrator.ArchiveSweep(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (archived > 0)
                {
                    log.LogInformation("Archive sweep: {Count} world(s) archived after {Months} months of inactivity.",
                        archived, config.ArchiveAfterMonths);
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Reaper pass failed (will retry).");
        }
    }
});

log.LogInformation(
    "WorldHost up on {Bind}:{Port} — domain {Domain}, image {Image}, quotas: {Worlds} worlds/account, {Players} players, idle {Idle} min.",
    config.BindAddress, config.Port, config.BaseDomain, config.ServerImage,
    config.MaxWorldsPerAccount, config.MaxPlayersPerWorld, config.IdleShutdownMinutes);

app.Run();

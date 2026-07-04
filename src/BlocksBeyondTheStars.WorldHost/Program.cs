// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.WorldHost;

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
var orchestrator = new WorldOrchestrator(config, registry, launcher);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");

var app = builder.Build();
var log = app.Logger;

// Resolves the caller's account from the Authorization: Bearer <session> header; null = not signed in.
AccountRecord? Caller(HttpContext ctx)
{
    string header = ctx.Request.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? registry.ResolveSession(header.Substring(prefix.Length).Trim())
        : null;
}

app.MapGet("/healthz", () => Results.Text("ok\n"));

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

app.MapPost("/api/signup", (SignupRequest req) =>
{
    var (ok, error, accountId, session) = registry.CreateAccount(req.Name, req.Password, req.ClaimCode);
    if (!ok)
    {
        return Results.BadRequest(new { error });
    }

    log.LogInformation("Account created: {Name} ({Id}).", req.Name, accountId);
    return Results.Json(new { accountId, sessionToken = session });
});

app.MapPost("/api/login", (SignupRequest req) =>
{
    if (registry.Login(req.Name, req.Password) is not { } login)
    {
        return Results.Unauthorized();
    }

    return Results.Json(new { accountId = login.AccountId, sessionToken = login.SessionToken });
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

    var (ok, error, world) = registry.CreateWorld(account.Id, req.Name);
    if (!ok)
    {
        return Results.BadRequest(new { error });
    }

    log.LogInformation("World '{Name}' ({Id}) created by {Account}.", world!.DisplayName, world.Id, account.Name);
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

    // Stop first so no orphan container keeps running under a deleted registry row. The saves volume is
    // intentionally NOT removed — an operator can still recover/export it; automated retention is Phase 3.
    orchestrator.StopWorld(world);
    registry.DeleteWorld(world.Id);
    log.LogInformation("World '{Name}' ({Id}) deleted by {Account} (saves volume retained).", world.DisplayName, world.Id, account.Name);
    return Results.Ok();
});

// ---------------- Background reaper ----------------

// Reconcile registry vs Docker every 30 s: instances that exited themselves (idle shutdown — the normal
// sleep path) get marked stopped so the next join wakes them and world lists stay truthful.
var reaper = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
    while (await timer.WaitForNextTickAsync())
    {
        try
        {
            int reaped = orchestrator.Reap();
            if (reaped > 0)
            {
                log.LogInformation("Reaper: {Count} idle-stopped world(s) marked stopped.", reaped);
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

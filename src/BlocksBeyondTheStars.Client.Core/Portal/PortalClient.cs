// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Portal
{
    public sealed class PortalLoginResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string SessionToken { get; set; } = string.Empty;

        /// <summary>The community rules changed since this account accepted them — the player must
        /// re-accept on the portal website before world actions succeed.</summary>
        public bool TermsOutdated { get; set; }
    }

    public sealed class PortalWorldInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public sealed class PortalWorldsResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public List<PortalWorldInfo> Worlds { get; set; } = new List<PortalWorldInfo>();
    }

    public sealed class PortalJoinResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string NativeHost { get; set; } = string.Empty;
        public int NativePort { get; set; }
        public string WssUrl { get; set; } = string.Empty;
        public string JoinToken { get; set; } = string.Empty;
    }

    public sealed class PortalSimpleResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Client for the hosted-worlds control plane ("WorldHost") — sign in, list/join your worlds, file a
    /// player report. Mirrors <see cref="Feedback.FeedbackUploader"/>: plain <see cref="HttpClient"/> +
    /// System.Text.Json so the exact same code runs in the Unity player AND the headless test suite;
    /// calls are synchronous and never throw (the Unity layer runs them on a background task). Desktop
    /// only — the browser client never selects servers (HOSTED_WORLDS.md).
    /// </summary>
    public sealed class PortalClient
    {
        public const string DefaultPortalUrl = "https://play.blocksbeyondthestars.de";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _baseUrl;
        private readonly HttpClient _http;

        public PortalClient(string? baseUrl = null, HttpClient? http = null)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultPortalUrl : baseUrl!.Trim().TrimEnd('/');
            // Generous timeout: joining may WAKE a sleeping world (container start + world load, up to
            // ~90 s server-side). Other calls return in milliseconds and are unaffected by the ceiling.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        public PortalLoginResult Login(string name, string password)
        {
            var (status, body) = Post("/api/login", new { name, password, acceptedTermsVersion = 0 }, session: null);
            return ParseLogin(status, body);
        }

        public PortalWorldsResult ListWorlds(string session)
        {
            var (status, body) = Get("/api/worlds", session);
            return ParseWorlds(status, body);
        }

        public PortalJoinResult JoinWorld(string session, string worldId, string playerName)
        {
            var (status, body) = Post($"/api/worlds/{worldId}/join", new { playerName }, session);
            return ParseJoin(status, body);
        }

        public PortalSimpleResult Report(string session, string reportedName, string category, string message)
        {
            var (status, body) = Post("/api/reports", new { reportedName, category, message }, session);
            return ParseSimple(status, body);
        }

        // ---------------- Response parsing (static + public: exercised directly by the test suite) ----------------

        public static PortalLoginResult ParseLogin(int status, string body)
        {
            var result = new PortalLoginResult();
            if (!Succeeded(status, body, out string error, out JsonDocument? doc))
            {
                result.Error = error;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.AccountId = GetString(doc!, "accountId");
                result.SessionToken = GetString(doc!, "sessionToken");
                result.TermsOutdated = doc!.RootElement.TryGetProperty("termsOutdated", out var to) && to.ValueKind == JsonValueKind.True;
            }

            return result;
        }

        public static PortalWorldsResult ParseWorlds(int status, string body)
        {
            var result = new PortalWorldsResult();
            if (!Succeeded(status, body, out string error, out JsonDocument? doc))
            {
                result.Error = error;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                if (doc!.RootElement.TryGetProperty("worlds", out var worlds) && worlds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in worlds.EnumerateArray())
                    {
                        result.Worlds.Add(new PortalWorldInfo
                        {
                            Id = GetString(w, "id"),
                            Name = GetString(w, "name"),
                            Status = GetString(w, "status"),
                        });
                    }
                }
            }

            return result;
        }

        public static PortalJoinResult ParseJoin(int status, string body)
        {
            var result = new PortalJoinResult();
            if (!Succeeded(status, body, out string error, out JsonDocument? doc))
            {
                result.Error = error;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.NativeHost = GetString(doc!, "nativeHost");
                result.WssUrl = GetString(doc!, "wssUrl");
                result.JoinToken = GetString(doc!, "joinToken");
                if (doc!.RootElement.TryGetProperty("nativePort", out var port) && port.TryGetInt32(out int p))
                {
                    result.NativePort = p;
                }
            }

            return result;
        }

        public static PortalSimpleResult ParseSimple(int status, string body)
        {
            var result = new PortalSimpleResult();
            if (!Succeeded(status, body, out string error, out JsonDocument? doc))
            {
                result.Error = error;
                return result;
            }

            doc?.Dispose();
            result.Ok = true;
            return result;
        }

        /// <summary>Shared success/error shape: 2xx = ok (body parsed into <paramref name="doc"/>); anything
        /// else surfaces the server's player-safe <c>{"error": …}</c> text, or a status code fallback.</summary>
        private static bool Succeeded(int status, string body, out string error, out JsonDocument? doc)
        {
            doc = null;
            try
            {
                doc = string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                // non-JSON body (proxy error page); fall through to the status handling
            }

            if (status is >= 200 and < 300)
            {
                error = string.Empty;
                return true;
            }

            error = doc != null ? GetString(doc, "error") : string.Empty;
            if (error.Length == 0)
            {
                error = status == 401 ? "unauthorized" : status == 0 ? "offline" : $"http_{status}";
            }

            doc?.Dispose();
            doc = null;
            return false;
        }

        private static string GetString(JsonDocument doc, string property) => GetString(doc.RootElement, property);

        private static string GetString(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        // ---------------- Transport ----------------

        private (int Status, string Body) Post(string path, object payload, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
                };
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty); // offline/timeout/DNS — parsed as "offline"
            }
        }

        private (int Status, string Body) Get(string path, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty);
            }
        }

        private static void Authorize(HttpRequestMessage request, string? session)
        {
            if (!string.IsNullOrEmpty(session))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session);
            }
        }
    }
}

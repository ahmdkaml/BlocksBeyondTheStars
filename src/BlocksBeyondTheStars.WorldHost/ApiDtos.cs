// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

// Request bodies of the WorldHost HTTP API (camelCase on the wire via the web JSON defaults).

/// <summary>Signup body; <paramref name="ClaimCode"/> is only needed (and only checked) when registering
/// a developer-reserved name. <paramref name="AcceptedTermsVersion"/> must carry the CURRENT rules version
/// (the signup UI sends it with its required checkbox) or the signup is refused.</summary>
public sealed record SignupRequest(string Name, string Password, string? ClaimCode = null, int AcceptedTermsVersion = 0);

public sealed record CreateWorldRequest(string Name);

public sealed record JoinRequestDto(string PlayerName);

/// <summary>Player report ("Spieler melden"): who misbehaved (in-game name), where, why. Categories:
/// chat, name, griefing, other.</summary>
public sealed record ReportRequest(string ReportedName, string Category, string? Message = null, string? WorldId = null);

public sealed record CloseReportRequest(string Status);

public sealed record BanRequest(string AccountId, bool Banned, string? Reason = null);

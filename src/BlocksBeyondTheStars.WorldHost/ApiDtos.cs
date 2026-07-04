// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

// Request bodies of the WorldHost HTTP API (camelCase on the wire via the web JSON defaults).

/// <summary>Signup body; <paramref name="ClaimCode"/> is only needed (and only checked) when registering
/// a developer-reserved name.</summary>
public sealed record SignupRequest(string Name, string Password, string? ClaimCode = null);

public sealed record CreateWorldRequest(string Name);

public sealed record JoinRequestDto(string PlayerName);

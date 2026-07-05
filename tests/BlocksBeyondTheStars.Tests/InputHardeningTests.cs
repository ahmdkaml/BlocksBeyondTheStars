// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player-input hardening (audit 2026-07-05): the native (MessagePack) decode path must reject oversized
/// packets, mirroring the WebSocket frame cap so native clients can't smuggle multi-MB payloads (e.g. a
/// giant FacePixels/Description blob) that the server persists and rebroadcasts.
/// </summary>
public sealed class InputHardeningTests
{
    [Fact]
    public void Decode_RejectsOversizedNativePacket()
    {
        // A well-formed tag byte followed by a body far above the cap: dropped (null), not deserialized.
        var oversized = new byte[NetCodec.MaxPacketBytes + 16];
        oversized[0] = 1; // some valid-looking tag; length check fires before tag lookup matters
        Assert.Null(NetCodec.Decode(oversized));
    }

    [Fact]
    public void Decode_AcceptsNormalSizedIntent()
    {
        // A real intent round-trips (regression guard: the size cap must not reject legitimate traffic).
        var encoded = NetCodec.Encode(new ChatIntent { Text = "hello" });
        Assert.True(encoded.Length < NetCodec.MaxPacketBytes);
        var decoded = Assert.IsType<ChatIntent>(NetCodec.Decode(encoded));
        Assert.Equal("hello", decoded.Text);
    }

    [Fact]
    public void MaxPacketBytes_MatchesJsonCap()
    {
        // Native and browser clients get the same ceiling; drift between them would reopen the gap.
        Assert.Equal(NetCodec.MaxJsonPayloadBytes, NetCodec.MaxPacketBytes);
    }
}

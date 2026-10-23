// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared transport the protocol and server-hardening suites drive by hand (#2781): it raises exactly the events
/// <c>GameServer.OnPayload</c> consumes — <see cref="Connect"/>, <see cref="Disconnect"/>, <see cref="Receive"/> for an
/// encoded message and <see cref="ReceiveRaw"/> for hand-built bytes — and records every message the server sends back
/// in <see cref="Sent"/>, decoded, with its connection (<c>int.MinValue</c> for a broadcast).
/// </summary>
internal sealed class DrivenTransport : IServerTransport
{
    public event Action<int>? ClientConnected;
    public event Action<int>? ClientDisconnected;
    public event Action<int, byte[]>? PayloadReceived;

    public readonly List<(int Conn, object Msg)> Sent = new();

    public void Connect(int id) => ClientConnected?.Invoke(id);
    public void Disconnect(int id) => ClientDisconnected?.Invoke(id);
    public void Receive(int id, object message) => PayloadReceived?.Invoke(id, NetCodec.Encode(message));
    public void ReceiveRaw(int id, byte[] payload) => PayloadReceived?.Invoke(id, payload);
    public void Start(int port) { }
    public void Poll() { }
    public void Stop() { }
    public void Dispose() { }

    public void Send(int connectionId, byte[] payload, DeliveryMode mode)
    {
        if (NetCodec.Decode(payload) is { } message) { Sent.Add((connectionId, message)); }
    }

    public void Broadcast(byte[] payload, DeliveryMode mode)
    {
        if (NetCodec.Decode(payload) is { } message) { Sent.Add((int.MinValue, message)); }
    }
}

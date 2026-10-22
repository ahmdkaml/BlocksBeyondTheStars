// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.


using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Minimal transport harness that raises exactly the events consumed by
/// GameServer.OnPayload and records messages sent by the server.
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

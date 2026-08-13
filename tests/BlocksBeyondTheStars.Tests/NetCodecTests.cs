// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Reflection;
using System.Text;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class NetCodecTests
{
    private const string MessageNamespace =
        "BlocksBeyondTheStars.Networking.Messages";

    [Fact]
    public void TopLevelMessages_HaveExactlyOneNetCodecTag()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var missing = topLevelMessages
            .Where(type => !NetCodec.RegisteredMessageTags.ContainsKey(type))
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Top-level messages without a NetCodec tag: " +
            string.Join(", ", missing.Select(type => type.FullName)));
    }

    [Fact]
    public void EveryNetCodecTag_MapsToATopLevelMessage()
    {
        var topLevelMessages = GetTopLevelMessageTypes();

        var nonTopLevelRegistrations = NetCodec.RegisteredMessages
            .Where(entry => !topLevelMessages.Contains(entry.Value))
            .OrderBy(entry => entry.Key)
            .ToArray();

        Assert.True(
            nonTopLevelRegistrations.Length == 0,
            "NetCodec tags that do not map to top-level messages: " +
            string.Join(
                ", ",
                nonTopLevelRegistrations.Select(
                    entry => $"{entry.Key} -> {entry.Value.FullName}")));
    }
    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughMessagePack()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var decoded = NetCodec.Decode(NetCodec.Encode(message));

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }
    [Fact]
    public void JoinRequest_PreservesFieldsThroughMessagePackRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.Encode(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }
    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughJson()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var payload = NetCodec.EncodeJson(message);
            var decoded = NetCodec.Decode(payload);

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }
    [Fact]
    public void JoinRequest_PreservesFieldsThroughJsonRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.EncodeJson(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }
    [Fact]
    public void MutatedMessagePackPayload_NeverThrows()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var original = NetCodec.Encode(message);

            // Truncate the payload at every possible boundary after the tag.
            for (int length = 0; length < original.Length; length++)
            {
                var truncated = original[..length];

                var exception = Record.Exception(() => NetCodec.Decode(truncated));

                Assert.Null(exception);
            }

            // Flip each byte individually.
            for (int index = 0; index < original.Length; index++)
            {
                var mutated = (byte[])original.Clone();
                mutated[index] ^= 0xFF;

                var exception = Record.Exception(() => NetCodec.Decode(mutated));

                Assert.Null(exception);
            }
        }
    }

    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughMessagePack()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var decoded = NetCodec.Decode(NetCodec.Encode(message));

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }

    [Fact]
    public void JoinRequest_PreservesFieldsThroughMessagePackRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.Encode(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }

    [Fact]
    public void EveryRegisteredMessage_RoundTripsThroughJson()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var payload = NetCodec.EncodeJson(message);
            var decoded = NetCodec.Decode(payload);

            Assert.NotNull(decoded);
            Assert.Equal(type, decoded.GetType());
        }
    }

    [Fact]
    public void JoinRequest_PreservesFieldsThroughJsonRoundTrip()
    {
        var original = new JoinRequest
        {
            ProtocolVersion = 123,
            PlayerName = "TestPlayer",
            Password = "secret",
            Token = "test-token",
            HostedToken = "host-token",
            Locale = "de",
            ViewDistanceChunks = 12,
        };

        var decoded = Assert.IsType<JoinRequest>(
            NetCodec.Decode(NetCodec.EncodeJson(original)));

        Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(original.PlayerName, decoded.PlayerName);
        Assert.Equal(original.Password, decoded.Password);
        Assert.Equal(original.Token, decoded.Token);
        Assert.Equal(original.HostedToken, decoded.HostedToken);
        Assert.Equal(original.Locale, decoded.Locale);
        Assert.Equal(original.ViewDistanceChunks, decoded.ViewDistanceChunks);
    }

    [Fact]
    public void MutatedMessagePackPayload_NeverThrows()
    {
        foreach (var type in NetCodec.RegisteredMessageTags.Keys)
        {
            var message = Activator.CreateInstance(type);

            Assert.NotNull(message);

            var original = NetCodec.Encode(message);

            // Truncate the payload at every possible boundary after the tag.
            for (int length = 0; length < original.Length; length++)
            {
                var truncated = original[..length];

                var exception = Record.Exception(() => NetCodec.Decode(truncated));

                Assert.Null(exception);
            }

            // Flip each byte individually.
            for (int index = 0; index < original.Length; index++)
            {
                var mutated = (byte[])original.Clone();
                mutated[index] ^= 0xFF;

                var exception = Record.Exception(() => NetCodec.Decode(mutated));

                Assert.Null(exception);
            }
        }
    }
    
    [Fact]
    public void TruncatedJsonEnvelope_NeverThrows()
    {
        var original = NetCodec.EncodeJson(
            new JoinRequest
            {
                ProtocolVersion = 123,
                PlayerName = "TestPlayer",
                Locale = "en",
            });

        // JSON envelope uses the dedicated tag 255.
        Assert.Equal(255, original[0]);

        // Try every truncated prefix, including the tag-only payload.
        for (int length = 0; length < original.Length; length++)
        {
            var truncated = original[..length];

            var exception = Record.Exception(() => NetCodec.Decode(truncated));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void MalformedJsonEnvelopes_AreRejectedWithoutThrowing()
    {
        var malformedPayloads = new[]
        {
            new byte[] { 255 },
            JsonPayload("{"),
            JsonPayload("{\"body\":{}}"),
            JsonPayload("{\"tag\":1}"),
            JsonPayload("{\"tag\":\"not-a-number\",\"body\":{}}"),
            JsonPayload("{\"tag\":256,\"body\":{}}"),
            JsonPayload("{\"tag\":254,\"body\":{}}"),
            JsonPayload("{\"tag\":1,\"body\":{"),
        };

        foreach (var payload in malformedPayloads)
        {
            var exception = Record.Exception(() => NetCodec.Decode(payload));

            Assert.Null(exception);
            Assert.Null(NetCodec.Decode(payload));
        }
    }

    private static byte[] JsonPayload(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var payload = new byte[body.Length + 1];
        payload[0] = 255;
        Buffer.BlockCopy(body, 0, payload, 1, body.Length);
        return payload;
    }

    [Fact]
    public void DeeplyNestedJsonBody_IsRejectedWithoutThrowing()
    {
        const int depth = 100;

        var body = new StringBuilder();

        for (int i = 0; i < depth; i++)
        {
            body.Append("{\"nested\":");
        }

        body.Append("{}");

        for (int i = 0; i < depth; i++)
        {
            body.Append('}');
        }

        var json =
            "{\"tag\":1,\"body\":" +
            body +
            "}";

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var payload = new byte[jsonBytes.Length + 1];

        payload[0] = 255;
        Buffer.BlockCopy(jsonBytes, 0, payload, 1, jsonBytes.Length);

        var exception = Record.Exception(() => NetCodec.Decode(payload));

        Assert.Null(exception);
        Assert.Null(NetCodec.Decode(payload));
    }

    [Fact]
    public void MutatedJsonPayload_NeverThrows()
    {
        var original = NetCodec.EncodeJson(
            new JoinRequest
            {
                ProtocolVersion = 123,
                PlayerName = "TestPlayer",
                Locale = "en",
            });

        Assert.Equal(255, original[0]);

        // Truncate at every possible boundary.
        for (int length = 0; length < original.Length; length++)
        {
            var truncated = original[..length];

            var exception = Record.Exception(() => NetCodec.Decode(truncated));

            Assert.Null(exception);
        }

        // Flip every byte individually.
        for (int index = 0; index < original.Length; index++)
        {
            var mutated = (byte[])original.Clone();
            mutated[index] ^= 0xFF;

            var exception = Record.Exception(() => NetCodec.Decode(mutated));

            Assert.Null(exception);
        }
    }
    private static HashSet<Type> GetTopLevelMessageTypes()
    {
        var messageTypes = GetMessageTypes();
        var referencedTypes = new HashSet<Type>();

        foreach (var messageType in messageTypes)
        {
            foreach (var property in messageType.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public))
            {
                CollectMessageTypes(property.PropertyType, referencedTypes);
            }
        }

        return messageTypes
            .Where(type => !referencedTypes.Contains(type))
            .ToHashSet();
    }

    private static Type[] GetMessageTypes()
    {
        return typeof(NetCodec).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.Namespace != null &&
                (type.Namespace == MessageNamespace ||
                 type.Namespace.StartsWith(
                     MessageNamespace + ".",
                     StringComparison.Ordinal)))
            .ToArray();
    }

    private static void CollectMessageTypes(
        Type type,
        HashSet<Type> referencedTypes)
    {
        if (type.IsArray)
        {
            CollectMessageTypes(type.GetElementType()!, referencedTypes);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                CollectMessageTypes(argument, referencedTypes);
            }
        }

        if (type.Namespace == MessageNamespace ||
            (type.Namespace?.StartsWith(
                MessageNamespace + ".",
                StringComparison.Ordinal) ?? false))
        {
            if (type.IsClass)
            {
                referencedTypes.Add(type);
            }
        }
    }
}

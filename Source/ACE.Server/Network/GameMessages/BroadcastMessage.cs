using System;
using ACE.Common.Cryptography;

namespace ACE.Server.Network.GameMessages
{
    // A broadcast captures the serialized bytes once. Queued packets, including retries,
    // own a read-only slice and their own sequence header.
    internal sealed class BroadcastMessage : GameMessage
    {
        private readonly byte[] bytes;
        private readonly uint firstChecksum;
        private readonly uint[] otherChecksums;

        internal BroadcastMessage(GameMessage message) : this(message, message.Data.ToArray()) { }

        private BroadcastMessage(GameMessage message, byte[] bytes) : base(message.Opcode, message.Group, bytes)
        {
            this.bytes = bytes;
            int count = (bytes.Length + PacketFragment.MaxFragmentDataSize - 1) / PacketFragment.MaxFragmentDataSize;
            firstChecksum = Hash32.Calculate(bytes, 0, Math.Min(bytes.Length, PacketFragment.MaxFragmentDataSize));
            if (count > 1)
            {
                otherChecksums = new uint[count - 1];
                for (int i = 1; i < count; i++)
                {
                    int offset = i * PacketFragment.MaxFragmentDataSize;
                    otherChecksums[i - 1] = Hash32.Calculate(bytes, offset, Math.Min(PacketFragment.MaxFragmentDataSize, bytes.Length - offset));
                }
            }
        }

        internal ServerPacketFragment CreateFragment(ushort index, int offset, int length)
        {
            uint checksum = index == 0 ? firstChecksum : otherChecksums[index - 1];
            return new ServerPacketFragment(bytes.AsMemory(offset, length), checksum);
        }

        internal static GameMessage[] Capture(GameMessage[] messages)
        {
            var captured = new GameMessage[messages.Length];
            for (int i = 0; i < messages.Length; i++)
                captured[i] = new BroadcastMessage(messages[i]);
            return captured;
        }
    }
}

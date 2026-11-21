using System;
using System.Threading;

using ACE.Common.Cryptography;

namespace ACE.Server.Network
{
    public class ServerPacketFragment : PacketFragment
    {
        private readonly ReadOnlyMemory<byte> sharedData;
        private readonly uint sharedChecksum;

        internal ServerPacketFragment(ReadOnlyMemory<byte> data, uint checksum)
        {
            sharedData = data;
            sharedChecksum = checksum;
        }

        // Public callers can edit Data. Give those callers a private copy without
        // changing the bytes or checksums queued for any other session.
        public override byte[] Data
        {
            get
            {
                var owned = Volatile.Read(ref ownedData);
                if (owned != null || sharedData.IsEmpty)
                    return owned;
                var copy = sharedData.ToArray();
                return Interlocked.CompareExchange(ref ownedData, copy, null) ?? copy;
            }
            protected set => base.Data = value;
        }

        public override int Length => PacketFragmentHeader.HeaderSize + (sharedData.IsEmpty ? ownedData.Length : sharedData.Length);

        public ServerPacketFragment(byte[] data)
        {
            Data = data;
        }

        /// <summary>
        /// Returns the Hash32 of the payload added to buffer
        /// </summary>
        public uint PackAndReturnHash32(byte[] buffer, ref int offset)
        {
            Header.Size = (ushort)Length;

            var headerHash32 = Header.PackAndReturnHash32(buffer, ref offset);

            var data = Volatile.Read(ref ownedData);
            if (data == null)
            {
                sharedData.Span.CopyTo(buffer.AsSpan(offset));
                offset += sharedData.Length;
                return headerHash32 + sharedChecksum;
            }

            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            offset += data.Length;

            return headerHash32 + Hash32.Calculate(data, data.Length);
        }
    }
}

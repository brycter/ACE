using System;
using System.Linq;
using System.Threading.Tasks;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests
{
    [TestClass]
    public class BroadcastTests
    {
        private sealed class TestMessage : GameMessage
        {
            public TestMessage(byte[] data) : base(GameMessageOpcode.None, GameMessageGroup.SmartboxQueue)
            {
                Writer.Write(data);
            }
        }

        [DataTestMethod]
        [DataRow(1)]
        [DataRow(96)]
        [DataRow(448)]
        [DataRow(449)]
        [DataRow(896)]
        [DataRow(1097)]
        public void BroadcastMatchesUnicastForIndependentSessionsAndRetries(int length)
        {
            var bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 17)).ToArray();
            var source = new TestMessage(bytes);
            var broadcast = new BroadcastMessage(source);
            source.Data.Position = 0;
            source.Data.WriteByte(255);

            Parallel.For(0, 64, session =>
            {
                var normal = new MessageFragment(new TestMessage(bytes), (uint)session + 100);
                var shared = new MessageFragment(broadcast, (uint)session + 100);
                var count = normal.Count;
                for (int i = 0; i < count; i++)
                {
                    bool tail = i == 0 && count > 1 && session % 2 == 0;
                    var reference = tail ? normal.GetTailFragment() : normal.GetNextFragment();
                    var actual = tail ? shared.GetTailFragment() : shared.GetNextFragment();
                    Assert.AreEqual(reference.Length, actual.Length);
                    AssertEqualPacket(reference, actual);
                    AssertEqualPacket(reference, actual); // retransmit
                }
                Assert.AreEqual(0, shared.DataRemaining);
            });
        }

        [TestMethod]
        public void MutablePublicFragmentDataIsPrivateToItsRecipient()
        {
            var message = new BroadcastMessage(new TestMessage(new byte[] { 1, 2, 3 }));
            var first = new MessageFragment(message, 1).GetNextFragment();
            var second = new MessageFragment(message, 1).GetNextFragment();
            first.Data[0] = 9;
            var expected = new MessageFragment(new TestMessage(new byte[] { 9, 2, 3 }), 1).GetNextFragment();
            AssertEqualPacket(expected, first);
            expected = new MessageFragment(new TestMessage(new byte[] { 1, 2, 3 }), 1).GetNextFragment();
            AssertEqualPacket(expected, second);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, second.Data);
        }

        private static void AssertEqualPacket(ServerPacketFragment expected, ServerPacketFragment actual)
        {
            var normal = new byte[512];
            var shared = new byte[512];
            int firstOffset = 12, secondOffset = 12;
            Assert.AreEqual(expected.PackAndReturnHash32(normal, ref firstOffset), actual.PackAndReturnHash32(shared, ref secondOffset));
            Assert.AreEqual(firstOffset, secondOffset);
            CollectionAssert.AreEqual(normal, shared);
        }
    }
}

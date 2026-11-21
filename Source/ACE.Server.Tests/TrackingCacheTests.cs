using Microsoft.VisualStudio.TestTools.UnitTesting;
using ACE.Server.Physics;

namespace ACE.Server.Tests
{
    [TestClass]
    public class TrackingCacheTests
    {
        [TestMethod]
        public void EmptyTrackingMapsAreIndependentAndMaintainInversePlayerMembership()
        {
            var player = new PhysicsObj { ID = 0x50000001 };
            var target = new PhysicsObj { ID = 0x80000001 };
            var unrelated = new PhysicsObj { ID = 0x80000002 };

            Assert.AreEqual(0, target.ObjMaint.GetKnownPlayersCount());
            Assert.IsFalse(target.ObjMaint.RemoveKnownPlayer(player));
            Assert.IsTrue(player.ObjMaint.AddKnownObject(target));
            Assert.IsFalse(player.ObjMaint.AddKnownObject(target));
            Assert.AreSame(target, player.ObjMaint.GetKnownObject(target.ID));
            Assert.AreEqual(1, target.ObjMaint.GetKnownPlayersCount());
            Assert.AreEqual(0, unrelated.ObjMaint.GetKnownPlayersCount());
            Assert.IsTrue(player.ObjMaint.RemoveKnownObject(target));
            Assert.AreEqual(0, target.ObjMaint.GetKnownPlayersCount());
            Assert.IsNull(player.ObjMaint.GetKnownObject(target.ID));
        }
    }
}

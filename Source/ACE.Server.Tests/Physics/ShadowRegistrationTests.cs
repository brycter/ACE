using ACE.Server.Physics;
using ACE.Server.Physics.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ACE.Server.Tests.Physics
{
    [TestClass]
    public class ShadowRegistrationTests
    {
        private static CellArray Occupied(params ObjCell[] cells)
        {
            var result = new CellArray();
            foreach (var cell in cells)
                result.add_cell(cell.ID, cell);
            return result;
        }

        [TestMethod]
        public void RepeatedMovesKeepLiveMembershipAndCollisionPriorityForParentAndChild()
        {
            var parent = new PhysicsObj { ID = 1 };
            var child = new PhysicsObj { ID = 2 };
            parent.Children.AddChild(child, child.Position.Frame, 0, 0);
            var other = new PhysicsObj { ID = 3 };
            var first = new ObjCell(0x1F60100);
            var second = new ObjCell(first.ID + 1);
            var overlap = Occupied(first, second);
            parent.add_shadows_to_cell(overlap);
            var parentShadow = parent.ShadowObjects[second.ID];
            var childShadow = child.ShadowObjects[second.ID];
            other.add_shadows_to_cell(second);
            parent.add_shadows_to_cell(Occupied(second));

            Assert.AreEqual(0, first.NumShadowObjects);
            Assert.AreEqual(1, parent.ShadowObjects.Count);
            Assert.AreEqual(1, child.ShadowObjects.Count);
            Assert.AreSame(parentShadow, parent.ShadowObjects[second.ID]);
            Assert.AreSame(childShadow, child.ShadowObjects[second.ID]);
            CollectionAssert.AreEqual(new[] { other.ShadowObjects[second.ID], parentShadow, childShadow }, second.ShadowObjectList);

            parent.remove_shadows_from_cells();
            Assert.AreEqual(0, parent.ShadowObjects.Count);
            Assert.AreEqual(0, child.ShadowObjects.Count);
            Assert.AreEqual(1, second.NumShadowObjects);
            second.release_objects();
            Assert.AreEqual(0, other.ShadowObjects.Count);
            Assert.AreEqual(0, second.NumShadowObjects);
        }

        [TestMethod]
        public void RebuiltCellWithTheSameIdReplacesTheOldRegistration()
        {
            var obj = new PhysicsObj();
            var first = new ObjCell(0x1F60100);
            var rebuilt = new ObjCell(first.ID);
            obj.add_shadows_to_cell(Occupied(first));
            obj.add_shadows_to_cell(Occupied(rebuilt));
            Assert.AreEqual(0, first.ShadowObjectList.Count);
            Assert.AreEqual(1, rebuilt.ShadowObjectList.Count);
            Assert.AreSame(rebuilt, obj.ShadowObjects[first.ID].Cell);
        }

        [TestMethod]
        public void DetachingAnUnlistedParticleShadowPreservesOtherCellObjects()
        {
            var cell = new ObjCell(0x1F60100);
            var obj = new PhysicsObj();
            obj.add_shadows_to_cell(cell);
            var emitter = new PhysicsObj { State = ACE.Entity.Enum.PhysicsState.ParticleEmitter, CurCell = cell };
            var occupied = Occupied(cell);
            emitter.add_shadows_to_cell(occupied);
            emitter.add_shadows_to_cell(occupied);
            emitter.remove_shadows_from_cells();
            Assert.AreEqual(0, emitter.ShadowObjects.Count);
            Assert.AreEqual(1, cell.NumShadowObjects);
            Assert.AreSame(obj.ShadowObjects[cell.ID], cell.ShadowObjectList[0]);
        }

        [TestMethod]
        public void TouringCellsDoesNotRetainDepartedCells()
        {
            var obj = new PhysicsObj();
            var occupied = new CellArray();
            for (uint i = 0; i < 256; i++)
            {
                if (i % 2 == 0) occupied.SetStatic();
                else occupied.SetDynamic();
                var cell = new ObjCell(0x1F60100 + i);
                occupied.add_cell(cell.ID, cell);
                obj.add_shadows_to_cell(occupied);
                Assert.AreEqual(1, occupied.Cells.Count);
                Assert.AreEqual(1, obj.ShadowObjects.Count);
            }
            obj.remove_shadows_from_cells();
            Assert.AreEqual(0, obj.ShadowObjects.Count);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using ColonySim.Simulation.Pawns;

namespace ColonySim.Simulation.Tests
{
    public class PawnManagerTests
    {
        [Test]
        public void SpawnPawn_ReturnsUniqueIncreasingIds()
        {
            var manager = new PawnManager();
            int first = manager.SpawnPawn(0, 0);
            int second = manager.SpawnPawn(1, 1);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void GetPawn_UnknownId_ReturnsNull()
        {
            var manager = new PawnManager();
            Assert.IsNull(manager.GetPawn(12345));
        }

        [Test]
        public void RemovePawn_ThenGetPawn_ReturnsNull()
        {
            var manager = new PawnManager();
            int id = manager.SpawnPawn(0, 0);
            bool removed = manager.RemovePawn(id);
            Assert.IsTrue(removed);
            Assert.IsNull(manager.GetPawn(id));
        }

        [Test]
        public void Tick_OnlyInvokesCallbackForPawnsInCurrentStaggerBucket()
        {
            var manager = new PawnManager();
            int idZero = manager.SpawnPawn(0, 0);  // bucket 0
            int idOne = manager.SpawnPawn(0, 0);   // bucket 1 (assuming sequential ids 0,1,...)
            var visited = new List<int>();

            manager.Tick(currentTick: 0, staggerBucketCount: 2, onStaggeredPawn: p => visited.Add(p.Id));

            Assert.Contains(idZero, visited);
            Assert.IsFalse(visited.Contains(idOne));
        }

        [Test]
        public void Tick_ZeroPawns_DoesNotThrow()
        {
            var manager = new PawnManager();
            Assert.DoesNotThrow(() => manager.Tick(0, 4, p => { }));
        }
    }
}

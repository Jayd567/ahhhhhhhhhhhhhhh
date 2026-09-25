using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using ColonySim.Data;
using ColonySim.Simulation.Grid;
using ColonySim.Simulation.Resources;

namespace ColonySim.Simulation.Tests
{
    public class TerrainDefBlobTests
    {
        private World _world;

        [SetUp]
        public void SetUp() => _world = new World("TerrainDefBlobTests");

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void PopulateBaseCosts_WalkableTerrainUsesDefCost_UnwalkableIsImpassable()
        {
            EntityManager em = _world.EntityManager;
            Entity grid = GridBootstrap.CreateGrid(em, 2, 1);
            DynamicBuffer<CellElement> cells = em.GetBuffer<CellElement>(grid);
            cells[0] = new CellElement { Value = new CellData { TerrainId = 1, Walkable = true } };
            cells[1] = new CellElement { Value = new CellData { TerrainId = 2, Walkable = false, HasRock = true } };

            var dirtDef = ScriptableObject.CreateInstance<TerrainDefSO>();
            dirtDef.TerrainTypeId = 1; dirtDef.IsWalkable = true;
            var rockWallDef = ScriptableObject.CreateInstance<TerrainDefSO>();
            rockWallDef.TerrainTypeId = 2; rockWallDef.IsWalkable = false;

            var blob = TerrainDefBlobBuilder.Build(new[] { dirtDef, rockWallDef });
            TerrainDefBlobBuilder.PopulateBaseCosts(em, grid, blob);

            DynamicBuffer<BaseCostElement> baseCosts = em.GetBuffer<BaseCostElement>(grid);
            Assert.AreEqual(1, baseCosts[0].Value);
            Assert.AreEqual(TileCost.Impassable, baseCosts[1].Value);

            blob.Dispose();
            Object.DestroyImmediate(dirtDef);
            Object.DestroyImmediate(rockWallDef);
        }
    }
}

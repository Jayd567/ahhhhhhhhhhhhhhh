using System;
using System.Collections.Generic;

namespace ColonySim.Simulation.Pawns
{
    public class PawnManager
    {
        private readonly Dictionary<int, Pawn> _pawnsById = new Dictionary<int, Pawn>();
        private readonly List<Pawn> _pawnList = new List<Pawn>();
        private int _nextId;
        private int _pathCapacity;
        public void PreparePaths(int capacity) { _pathCapacity = capacity; foreach (var pawn in _pawnList) pawn.CurrentPath.Capacity = capacity; }

        public int PawnCount => _pawnList.Count;

        public int SpawnPawn(float x, float y)
        {
            var pawn = new Pawn { Id = _nextId++, PositionX = x, PositionY = y };
            pawn.CurrentPath.Capacity = _pathCapacity;
            _pawnsById[pawn.Id] = pawn;
            _pawnList.Add(pawn);
            return pawn.Id;
        }

        public Pawn GetPawn(int id)
        {
            return _pawnsById.TryGetValue(id, out Pawn pawn) ? pawn : null;
        }

        public bool RemovePawn(int id)
        {
            if (!_pawnsById.TryGetValue(id, out Pawn pawn)) return false;
            _pawnsById.Remove(id);
            _pawnList.Remove(pawn);
            return true;
        }

        public void Tick(int currentTick, int staggerBucketCount, Action<Pawn> onStaggeredPawn)
        {
            for (int i = 0; i < _pawnList.Count; i++)
            {
                Pawn pawn = _pawnList[i];
                if (pawn.Id % staggerBucketCount == currentTick % staggerBucketCount)
                    onStaggeredPawn(pawn);
            }
        }
    }
}

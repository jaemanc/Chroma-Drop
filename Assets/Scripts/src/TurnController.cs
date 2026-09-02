// TurnController.cs — 한 턴의 흐름. 배치 가능 여부를 매 턴 검사하고,
// 놓을 데가 없으면 정해진 경로로 빠져나간다 (조각 리롤 → 그래도 없으면 종료).

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public enum NoMoveAction { Reroll, EndGame }

    public sealed class TurnController
    {
        readonly GameEngine eng;
        readonly List<PieceShape> shapes;
        readonly Rng spawnRng;
        int rerollBudget;

        public PieceShape Current { get; private set; }
        public int RerollsLeft { get { return rerollBudget; } }

        public TurnController(GameEngine eng, List<PieceShape> shapes, Rng spawnRng, int rerollCount)
        {
            this.eng = eng;
            this.shapes = shapes;
            this.spawnRng = spawnRng;
            rerollBudget = rerollCount;
            Current = shapes[spawnRng.Next(shapes.Count)];
        }

        /// <summary>지금 조각을 어디든 놓을 수 있는가.</summary>
        public bool CanPlaceAnywhere()
        {
            for (int i = 0; i < eng.Count; i++)
                if (PlacementAt(i) != null) return true;
            return false;
        }

        /// <summary>이 칸을 기준으로 놓을 수 있으면 칸 목록, 아니면 null.</summary>
        public List<int> PlacementAt(int originId)
        {
            var cells = PieceShapes.Resolve(eng.Topo, Current, originId);
            if (cells == null) return null;
            foreach (int id in cells)
                if (CellState.IsObstacle(eng.Get(id))) return null;   // 장애물 칸에는 못 놓는다
            return cells;
        }

        /// <summary>놓을 데가 없을 때의 처리 경로.</summary>
        public NoMoveAction OnNoPlacement()
        {
            if (rerollBudget <= 0) return NoMoveAction.EndGame;
            rerollBudget--;
            Current = shapes[spawnRng.Next(shapes.Count)];
            return NoMoveAction.Reroll;
        }

        /// <summary>다음 조각을 뽑는다. 매 턴 배치 가능 여부를 검사한다.</summary>
        public NoMoveAction Advance()
        {
            Current = shapes[spawnRng.Next(shapes.Count)];
            int guard = shapes.Count * 4;
            while (!CanPlaceAnywhere() && guard-- > 0)
            {
                var action = OnNoPlacement();
                if (action == NoMoveAction.EndGame) return NoMoveAction.EndGame;
            }
            return NoMoveAction.Reroll;
        }
    }
}

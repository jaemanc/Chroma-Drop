// EngineSpec.cs — 엔진이 받는 설정. 값은 전부 stages.json 에서 온다.

using System.Collections.Generic;

namespace ChromaDrop.Engine
{
    public enum FillKind { BottomUp, Random, Preset }
    public enum RefillKind { None, Drip, Instant }

    public struct RefillSpec
    {
        public RefillKind Mode;
        public int BlocksPerClear;
        public int DelayMs;
    }

    public sealed class ObstacleGroup
    {
        public string Type;
        public int Cell;
        public int HitsToBreak;
        public List<int> CellIds = new List<int>();
    }

    public sealed class EngineSpec
    {
        public int PaletteSize;
        public int MinGroupSize;
        public bool ChainReaction;
        public double InitialFillRatio;
        public FillKind FillPattern;
        public RefillSpec Refill;
        public double[] ColorWeights;
        public List<ObstacleGroup> Obstacles = new List<ObstacleGroup>();
    }
}

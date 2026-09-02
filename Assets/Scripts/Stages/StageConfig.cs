// StageConfig.cs — 스테이지 설정의 자료형. 코드가 아니라 설정이 난이도의 유일한 출처다.
// 이 파일에는 스테이지 번호도, 목표 수치도 리터럴로 들어가지 않는다.
// UnityEngine 에 의존하지 않는다 — 코어와 같은 이유다 (서버 검증·오프라인 툴 재사용).

using System;
using System.Collections.Generic;

namespace ColorMatcher.Core
{
    /// <summary>보드를 처음 어떤 순서로 채울지.</summary>
    public enum FillPattern { BottomUp, Random, Preset }

    /// <summary>매치 후 신규 블록 투입 방식.</summary>
    public enum RefillMode { None, Drip, Instant }

    /// <summary>목표 종류.</summary>
    public enum ObjectiveType { ClearCount, ClearColor, BreakObstacle, ReachScore }

    /// <summary>목표를 전부 채워야 하는지, 하나만 채우면 되는지.</summary>
    public enum ObjectiveMode { All, Any }

    public struct RefillPolicy
    {
        public RefillMode Mode;
        public int BlocksPerClear;   // drip 모드에서 한 번에 들어오는 개수
        public int DelayMs;          // 연출용 — 규칙에는 영향이 없다

        public static RefillPolicy Instant()
        {
            return new RefillPolicy { Mode = RefillMode.Instant, BlocksPerClear = 0, DelayMs = 0 };
        }
    }

    /// <summary>장애물 한 무리. 자리는 설정이 준다 — 코드가 배치를 정하지 않는다.</summary>
    public class ObstacleSpec
    {
        public string Type;             // brick | locked | frozen
        public int Cell;                // Board 의 칸 상수로 환산한 값
        public int HitsToBreak;         // brick 전용
        public List<Point> Positions = new List<Point>();
    }

    /// <summary>목표 하나.</summary>
    public class Objective
    {
        public ObjectiveType Type;
        public int ColorIndex = -1;     // clear_color 전용
        public string ColorName;        // 표시용
        public string ObstacleType;     // break_obstacle 전용
        public int Target;
    }

    /// <summary>Board 를 만들 때 필요한 것만 추린 설정. 코어가 아는 유일한 형태다.</summary>
    public class BoardSetup
    {
        public int Width, Height;
        public int MinMatch;
        public int ColorCount;
        public double InitialFillRatio;
        public FillPattern FillPattern;
        public List<ObstacleSpec> Obstacles = new List<ObstacleSpec>();
        public double[] ColorWeights;
        public RefillPolicy Refill;

        /// <summary>설정을 못 읽었을 때 쓰는 기본값. 타임어택처럼 스테이지가 없는 모드가 쓴다.</summary>
        public static BoardSetup Default(int colorCount)
        {
            return new BoardSetup
            {
                Width = Defaults.Width,
                Height = Defaults.Height,
                MinMatch = Defaults.MinMatch,
                ColorCount = colorCount,
                InitialFillRatio = 1.0,
                FillPattern = FillPattern.Random,
                Refill = RefillPolicy.Instant(),
            };
        }
    }

    /// <summary>설정을 하나도 못 읽었을 때만 쓰는 최후의 기본값.
    /// 스테이지 난이도가 아니라 '판이 성립하는 최소 조건'이다.</summary>
    public static class Defaults
    {
        public const int Width = 14;
        public const int Height = 14;
        public const int MinMatch = 2;
    }

    /// <summary>스테이지 한 판의 전체 설정. stages.json 한 항목과 1:1 대응한다.</summary>
    public class StageConfig
    {
        public int StageId;
        public bool Locked;             // true 면 CurveGenerator 가 덮어쓰지 않는다

        public int Width, Height;
        public double InitialFillRatio;
        public FillPattern FillPattern;
        public int[,] PresetLayout;     // fillPattern = preset 일 때만

        public RefillPolicy Refill;
        public double[] ColorWeights;
        public string[] ColorNames;

        public List<ObstacleSpec> Obstacles = new List<ObstacleSpec>();
        public List<Objective> Objectives = new List<Objective>();
        public ObjectiveMode ObjectiveMode;

        public int Moves;               // 0 이하면 제한 없음
        public int TimeSeconds;         // 0 이하면 제한 없음
        public int MinMatch;

        public BoardSetup ToBoardSetup(int colorCount)
        {
            return new BoardSetup
            {
                Width = Width,
                Height = Height,
                MinMatch = MinMatch,
                ColorCount = colorCount,
                InitialFillRatio = InitialFillRatio,
                FillPattern = FillPattern,
                Obstacles = Obstacles,
                ColorWeights = ColorWeights,
                Refill = Refill,
            };
        }

        /// <summary>장애물이 차지하는 칸 수.</summary>
        public int ObstacleCells
        {
            get
            {
                int n = 0;
                foreach (var o in Obstacles) n += o.Positions.Count;
                return n;
            }
        }

        /// <summary>조작 가능 칸 = 전체 칸 - 장애물 칸.</summary>
        public int PlayableCells { get { return Width * Height - ObstacleCells; } }
    }

    /// <summary>칸 종류 이름과 Board 상수의 대응. 이름은 설정 파일의 어휘다.</summary>
    public static class ObstacleKind
    {
        public const string Brick = "brick";
        public const string Locked = "locked";
        public const string Frozen = "frozen";

        public static bool TryCell(string type, out int cell)
        {
            switch (type)
            {
                case Brick: cell = Board.Obstacle; return true;
                case Locked: cell = Board.Wall; return true;
                case Frozen: cell = Board.Frozen; return true;
            }
            cell = 0;
            return false;
        }

        public static string NameOf(int cell)
        {
            if (cell == Board.Obstacle) return Brick;
            if (cell == Board.Wall) return Locked;
            if (cell == Board.Frozen) return Frozen;
            return null;
        }
    }
}

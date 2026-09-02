// NationRanking.cs — 랭킹 집계 (UnityEngine 비의존 순수 로직).
// 코어와 같은 이유로 엔진에 의존하지 않는다 — 서버에서 같은 코드를 돌려 검증할 수 있다.

using System.Collections.Generic;

/// <summary>랭킹 한 줄</summary>
public class ScoreEntry
{
    public string Uid;
    public string Name;
    public string Country;   // ISO 2글자. 미상은 "ZZ"
    public int Score;
    public long UpdatedMs;
}

/// <summary>국가 집계 한 줄</summary>
public class NationEntry
{
    public string Country;
    public int Total;        // 상위 TopPerNation 명의 점수 합
    public int Players;      // 그 나라 등록 인원수
    public int Best;         // 그 나라 1위 점수
}

public static class NationRanking
{
    /// <summary>국가 점수에 반영할 상위 인원 수. 인구가 많은 나라가 무조건 이기지 않게 상한을 둔다.</summary>
    public const int TopPerNation = 3;

    /// <summary>uid 당 최고점만 남긴다. 같은 점수면 먼저 세운 기록을 남긴다.</summary>
    public static List<ScoreEntry> DedupeBest(IEnumerable<ScoreEntry> rows)
    {
        var best = new Dictionary<string, ScoreEntry>();
        foreach (var r in rows)
        {
            if (r == null || string.IsNullOrEmpty(r.Uid)) continue;
            ScoreEntry cur;
            if (!best.TryGetValue(r.Uid, out cur)
                || r.Score > cur.Score
                || (r.Score == cur.Score && r.UpdatedMs < cur.UpdatedMs))
                best[r.Uid] = r;
        }
        var list = new List<ScoreEntry>(best.Values);
        SortDesc(list);
        return list;
    }

    /// <summary>국가별 집계. 상위 TopPerNation 명의 합으로 순위를 매긴다.</summary>
    public static List<NationEntry> Aggregate(IEnumerable<ScoreEntry> rows)
    {
        var byCountry = new Dictionary<string, List<ScoreEntry>>();
        foreach (var r in DedupeBest(rows))
        {
            string c = string.IsNullOrEmpty(r.Country) ? "ZZ" : r.Country;
            List<ScoreEntry> l;
            if (!byCountry.TryGetValue(c, out l)) byCountry[c] = l = new List<ScoreEntry>();
            l.Add(r);
        }

        var outp = new List<NationEntry>();
        foreach (var kv in byCountry)
        {
            var l = kv.Value;
            SortDesc(l);
            int total = 0;
            for (int i = 0; i < l.Count && i < TopPerNation; i++) total += l[i].Score;
            outp.Add(new NationEntry
            {
                Country = kv.Key,
                Total = total,
                Players = l.Count,
                Best = l[0].Score,
            });
        }

        // 합계 → 인원수 → 국가코드 순
        outp.Sort((a, b) =>
        {
            if (a.Total != b.Total) return b.Total.CompareTo(a.Total);
            if (a.Players != b.Players) return b.Players.CompareTo(a.Players);
            return string.CompareOrdinal(a.Country, b.Country);
        });
        return outp;
    }

    /// <summary>목록에서 특정 uid 의 순위(1부터). 없으면 0.</summary>
    public static int RankOf(List<ScoreEntry> sorted, string uid)
    {
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i].Uid == uid) return i + 1;
        return 0;
    }

    static void SortDesc(List<ScoreEntry> l)
    {
        l.Sort((a, b) =>
        {
            if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
            return a.UpdatedMs.CompareTo(b.UpdatedMs);   // 동점이면 먼저 세운 쪽이 위
        });
    }
}

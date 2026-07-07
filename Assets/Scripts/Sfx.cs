// Sfx.cs — 절차 생성 효과음. 오디오 에셋 의존 없음 (전부 런타임 PCM 생성).

using UnityEngine;

public class Sfx : MonoBehaviour
{
    const int SR = 22050;

    AudioSource src;
    AudioClip stamp, boom, item, expire, win, lose;

    void Awake()
    {
        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        stamp = Tone("sfx_stamp", 380, 0.07f, 0.55f);
        boom = NoiseBurst("sfx_boom", 0.22f, 0.7f);
        item = Arp("sfx_item", new[] { 780f, 1240f }, 0.07f, 0.45f);
        expire = Slide("sfx_expire", 340, 150, 0.20f, 0.5f);
        win = Arp("sfx_win", new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.5f);
        lose = Arp("sfx_lose", new[] { 392f, 311f, 262f }, 0.16f, 0.5f);
    }

    public void PlayStamp() { Play(stamp, Random.Range(0.95f, 1.05f)); }
    /// <summary>연쇄 단계가 높을수록 피치 상승</summary>
    public void PlayDestroy(int chain) { Play(boom, 1f + 0.14f * Mathf.Clamp(chain - 1, 0, 6)); }
    public void PlayItem() { Play(item, 1f); }
    public void PlayExpire() { Play(expire, 1f); }
    public void PlayWin() { Play(win, 1f); }
    public void PlayLose() { Play(lose, 1f); }

    void Play(AudioClip c, float pitch)
    {
        if (c == null) return;
        src.pitch = pitch;
        src.PlayOneShot(c, 0.9f);
    }

    static AudioClip FromSamples(string name, float[] d)
    {
        var c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    // 사각파+사인 혼합 블립
    static AudioClip Tone(string name, float freq, float dur, float vol)
    {
        int n = (int)(SR * dur);
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SR;
            float e = Mathf.Exp(-6f * t / dur);
            float s = Mathf.Sin(2 * Mathf.PI * freq * t);
            d[i] = (Mathf.Sign(s) * 0.5f + s * 0.5f) * vol * e;
        }
        return FromSamples(name, d);
    }

    // 주파수 슬라이드 (조각 시간 만료 등)
    static AudioClip Slide(string name, float f0, float f1, float dur, float vol)
    {
        int n = (int)(SR * dur);
        var d = new float[n];
        float ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float f = Mathf.Lerp(f0, f1, t);
            ph += 2 * Mathf.PI * f / SR;
            d[i] = Mathf.Sin(ph) * vol * Mathf.Exp(-4f * t);
        }
        return FromSamples(name, d);
    }

    // 저역 필터 노이즈 (파괴음)
    static AudioClip NoiseBurst(string name, float dur, float vol)
    {
        int n = (int)(SR * dur);
        var d = new float[n];
        var r = new System.Random(7);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float w = (float)(r.NextDouble() * 2 - 1);
            lp += (w - lp) * 0.25f;
            d[i] = lp * vol * Mathf.Exp(-5f * t);
        }
        return FromSamples(name, d);
    }

    // 음 연속 재생 (아이템/승리/패배 징글)
    static AudioClip Arp(string name, float[] notes, float step, float vol)
    {
        int per = (int)(SR * step);
        var d = new float[per * notes.Length];
        for (int k = 0; k < notes.Length; k++)
            for (int i = 0; i < per; i++)
            {
                float t = (float)i / SR;
                float e = Mathf.Exp(-5f * i / per);
                d[k * per + i] = Mathf.Sin(2 * Mathf.PI * notes[k] * t) * vol * e;
            }
        return FromSamples(name, d);
    }
}

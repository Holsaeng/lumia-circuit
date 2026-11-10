using UnityEngine;

/// 효과음을 코드로 합성한다 (외부 음원 없음).
public static class Sfx
{
    const int Rate = 44100;
    public static AudioClip Blip1, Blip2, Core, Zap, Death, Win, Bonus, Start;

    public static void Build()
    {
        Blip1 = Sweep("blip1", 0.07f, 720, 900, 0.25f, 0.5f);
        Blip2 = Sweep("blip2", 0.07f, 900, 1100, 0.25f, 0.5f);
        Core = Sweep("core", 0.5f, 220, 1200, 0.4f, 0.8f);
        Zap = Sweep("zap", 0.35f, 1400, 180, 0.4f, 0.9f);
        Death = Sweep("death", 1.1f, 700, 60, 0.45f, 1f, 0.25f);
        Bonus = Arp("bonus", new[] { 660f, 880f, 1320f }, 0.08f, 0.35f);
        Win = Arp("win", new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.11f, 0.4f);
        Start = Arp("start", new[] { 392f, 523f, 659f, 784f }, 0.12f, 0.35f);
    }

    static AudioClip Sweep(string name, float dur, float f0, float f1, float vol, float square, float noise = 0f)
    {
        int n = (int)(dur * Rate);
        var d = new float[n];
        float ph = 0;
        var rng = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            ph += Mathf.Lerp(f0, f1, t) / Rate;
            float s = Mathf.Sin(ph * Mathf.PI * 2);
            float sq = s >= 0 ? 1 : -1;
            float env = Mathf.Min(1, t * 40) * (1 - t);
            d[i] = (Mathf.Lerp(s, sq, square * 0.4f) + noise * ((float)rng.NextDouble() * 2 - 1)) * env * vol;
        }
        var c = AudioClip.Create(name, n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    static AudioClip Arp(string name, float[] notes, float step, float vol)
    {
        int per = (int)(step * Rate), n = per * notes.Length + Rate / 4;
        var d = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            int len = per + Rate / 4;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                float s = Mathf.Sin(i * notes[k] / Rate * Mathf.PI * 2) + 0.3f * Mathf.Sin(i * notes[k] * 2 / Rate * Mathf.PI * 2);
                d[k * per + i] += s * Mathf.Min(1, t * 60) * (1 - t) * (1 - t) * vol;
            }
        }
        var c = AudioClip.Create(name, n, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}

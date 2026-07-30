using System.Collections.Generic;
using UnityEngine;

// Resources/Audio의 효과음을 런타임 오브젝트로 재생한다.
// 각 호출마다 AudioSource를 하나 만들기 때문에 동시에 발생하는 효과음도 서로 끊지 않는다.
public static class GameSfx
{
    private const string ResourceFolder = "Audio/";
    private const float SilenceThreshold = 0.006f;
    private const float EdgePaddingSeconds = 0.02f;
    private static readonly Dictionary<string, AudioClip> Clips = new();
    private static readonly Dictionary<string, AudibleRange> AudibleRanges = new();
    private static readonly Dictionary<string, AudibleRange> LoudestRanges = new();
    private static readonly HashSet<string> MissingClips = new();

    // 설정 화면의 효과음 볼륨 슬라이더가 곱하는 전역 배율. 게임별 개별 볼륨(Play의 volume
    // 인자)과는 별개로 항상 곱해진다 - PlayerPrefs 로드/저장은 SettingsScreen이 담당한다.
    public static float Volume { get; set; } = 1f;

    public static void Play(string clipName, float volume = 1f, float maxDuration = 0f)
    {
        AudioClip clip = Load(clipName);
        if (clip == null) return;

        var player = new GameObject($"SFX_{clipName}");
        var source = player.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = Mathf.Clamp01(volume) * Mathf.Clamp01(Volume);
        source.clip = clip;
        AudibleRange range = FindAudibleRange(clipName, clip);
        if (maxDuration > 0f && maxDuration < range.Duration)
            range = FindLoudestRange(clipName, clip, range, maxDuration);
        source.time = range.Start;
        source.Play();

        Object.Destroy(player, Mathf.Max(0.01f, range.Duration));
    }

    private static AudioClip Load(string clipName)
    {
        if (Clips.TryGetValue(clipName, out AudioClip clip)) return clip;

        clip = Resources.Load<AudioClip>(ResourceFolder + clipName);
        if (clip != null)
        {
            // AudioImporter가 Preload Audio Data를 끈 상태여도 파형을 읽어 무음/최대 음량 구간을
            // 찾을 수 있도록 여기서 명시적으로 로드한다(짧은 효과음이며 Decompress On Load).
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            Clips.Add(clipName, clip);
            return clip;
        }

        if (MissingClips.Add(clipName))
            Debug.LogWarning($"효과음을 찾을 수 없습니다: Resources/{ResourceFolder}{clipName}");
        return null;
    }

    // 파일 앞뒤의 무음은 자동으로 건너뛰고 실제 파형이 있는 구간만 재생한다.
    private static AudibleRange FindAudibleRange(string clipName, AudioClip clip)
    {
        if (AudibleRanges.TryGetValue(clipName, out AudibleRange cached)) return cached;

        var fallback = new AudibleRange(0f, clip.length);
        int valueCount = clip.samples * clip.channels;
        if (valueCount <= 0)
        {
            AudibleRanges.Add(clipName, fallback);
            return fallback;
        }

        var samples = new float[valueCount];
        if (!clip.GetData(samples, 0))
        {
            AudibleRanges.Add(clipName, fallback);
            return fallback;
        }

        int first = -1;
        int last = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            if (Mathf.Abs(samples[i]) < SilenceThreshold) continue;
            if (first < 0) first = i;
            last = i;
        }

        if (first < 0)
        {
            AudibleRanges.Add(clipName, fallback);
            return fallback;
        }

        int padding = Mathf.RoundToInt(clip.frequency * clip.channels * EdgePaddingSeconds);
        first = Mathf.Max(0, first - padding);
        last = Mathf.Min(samples.Length - 1, last + padding);
        float start = (float)first / clip.channels / clip.frequency;
        float end = (float)(last + 1) / clip.channels / clip.frequency;
        var range = new AudibleRange(start, Mathf.Max(0.01f, end - start));
        AudibleRanges.Add(clipName, range);
        return range;
    }

    // 짧게 재생할 때는 단순히 파일 앞부분을 자르지 않고, 지정 길이 안에서 평균 음량이 가장
    // 큰 구간을 고른다. 생성형 효과음 앞쪽에 붙은 작은 잡음/긴 무음 때문에 소리가 안 들리는
    // 문제를 피하면서도 한 동작에 필요한 부분만 재생할 수 있다.
    private static AudibleRange FindLoudestRange(string clipName, AudioClip clip,
        AudibleRange audible, float duration)
    {
        string cacheKey = $"{clipName}:{duration:F3}";
        if (LoudestRanges.TryGetValue(cacheKey, out AudibleRange cached)) return cached;

        int channels = clip.channels;
        int frameCount = clip.samples;
        int windowFrames = Mathf.Clamp(Mathf.RoundToInt(duration * clip.frequency), 1, frameCount);
        int firstFrame = Mathf.Clamp(Mathf.FloorToInt(audible.Start * clip.frequency), 0, frameCount - 1);
        int lastFrameExclusive = Mathf.Clamp(
            Mathf.CeilToInt((audible.Start + audible.Duration) * clip.frequency), firstFrame + 1, frameCount);
        if (lastFrameExclusive - firstFrame <= windowFrames)
        {
            var wholeRange = new AudibleRange(audible.Start, Mathf.Min(duration, audible.Duration));
            LoudestRanges.Add(cacheKey, wholeRange);
            return wholeRange;
        }

        var samples = new float[frameCount * channels];
        if (!clip.GetData(samples, 0))
        {
            var fallback = new AudibleRange(audible.Start, duration);
            LoudestRanges.Add(cacheKey, fallback);
            return fallback;
        }

        double energy = 0d;
        for (int frame = firstFrame; frame < firstFrame + windowFrames; frame++)
            energy += FrameEnergy(samples, frame, channels);

        double bestEnergy = energy;
        int bestStartFrame = firstFrame;
        int finalStartFrame = lastFrameExclusive - windowFrames;
        for (int startFrame = firstFrame + 1; startFrame <= finalStartFrame; startFrame++)
        {
            energy -= FrameEnergy(samples, startFrame - 1, channels);
            energy += FrameEnergy(samples, startFrame + windowFrames - 1, channels);
            if (energy <= bestEnergy) continue;
            bestEnergy = energy;
            bestStartFrame = startFrame;
        }

        var range = new AudibleRange((float)bestStartFrame / clip.frequency, duration);
        LoudestRanges.Add(cacheKey, range);
        return range;
    }

    private static double FrameEnergy(float[] samples, int frame, int channels)
    {
        int offset = frame * channels;
        double energy = 0d;
        for (int channel = 0; channel < channels; channel++)
        {
            float value = samples[offset + channel];
            energy += value * value;
        }
        return energy;
    }

    private readonly struct AudibleRange
    {
        public readonly float Start;
        public readonly float Duration;

        public AudibleRange(float start, float duration)
        {
            Start = start;
            Duration = duration;
        }
    }
}

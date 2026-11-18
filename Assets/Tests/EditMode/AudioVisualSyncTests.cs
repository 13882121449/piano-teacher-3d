using NUnit.Framework;
using PianoTeacher3D.Piano;

/// <summary>
/// 音画同步的纯逻辑规则（2026-11-18 P5）。
/// <para>
/// 起音：所有音统一在「音符时刻 + <see cref="PianoAudioSynth.ScheduleLeadSeconds"/>」采样级起音，
/// 触发帧的相位（0～1 帧的迟到）被吸收，节奏不再随帧率抖动。
/// 键程：按力度 16～32 ms 到底，与 25 ms 的统一起音延迟对齐——声音在键到底时响起。
/// </para>
/// </summary>
public class AudioVisualSyncTests
{
    private const float Ms = 0.001f;

    [Test]
    public void OnsetDelay_AbsorbsFrameLateness()
    {
        float lead = PianoAudioSynth.ScheduleLeadSeconds;
        Assert.AreEqual(lead, PianoAudioSynth.OnsetDelay(1f, 1f, 1f), 1e-6f, "正点触发：等满提前量");
        Assert.AreEqual(lead - 10 * Ms, PianoAudioSynth.OnsetDelay(1f, 1.010f, 1f), 1e-5f, "晚 10 ms：少等 10 ms");
        Assert.AreEqual(0f, PianoAudioSynth.OnsetDelay(1f, 1.060f, 1f), 1e-6f, "晚于提前量：立即起音");
        Assert.AreEqual(lead, PianoAudioSynth.OnsetDelay(1f, 0.99f, 1f), 1e-6f, "提前触发也不超过提前量");
    }

    [Test]
    public void OnsetDelay_ConvertsTimelineLatenessToRealTimeAtSlowSpeed()
    {
        // 0.5× 速度下，时间轴晚 10 ms 相当于真实时间晚 20 ms
        float delay = PianoAudioSynth.OnsetDelay(2f, 2.010f, 0.5f);
        Assert.AreEqual(PianoAudioSynth.ScheduleLeadSeconds - 20 * Ms, delay, 1e-5f);
    }

    [Test]
    public void OnsetDelay_WithoutClock_PlaysImmediately()
    {
        Assert.AreEqual(0f, PianoAudioSynth.OnsetDelay(1f, float.NaN, 1f));
    }

    [Test]
    public void OnsetPlusFrameJitter_StaysWithinOneMillisecond()
    {
        // 同一首曲子里不同的帧相位（0～16 ms 迟到）得到的「真实起音时刻 − 音符时刻」应完全一致
        float note = 3.2f;
        float reference = -1f;
        for (int phase = 0; phase <= 16; phase++)
        {
            float clock = note + phase * Ms;
            float onset = (clock - note) + PianoAudioSynth.OnsetDelay(note, clock, 1f);
            if (reference < 0f) reference = onset;
            Assert.AreEqual(reference, onset, 1 * Ms, $"帧相位 {phase} ms 时起音偏移不一致");
        }
    }

    [Test]
    public void KeyTravel_IsFasterForLouderNotes_AndBracketsTheOnsetLead()
    {
        float soft = PianoKeyAnimator.PressSeconds(1);
        float loud = PianoKeyAnimator.PressSeconds(127);
        Assert.Less(loud, soft, "力度越大键程越快");
        Assert.AreEqual(PianoKeyAnimator.LoudPressSeconds, loud, 1e-4f);
        Assert.GreaterOrEqual(soft, PianoKeyAnimator.SoftPressSeconds * 0.98f);

        // 统一起音延迟落在键程范围内：中等力度（64）时键到底与起音相差不超过 8 ms
        float mid = PianoKeyAnimator.PressSeconds(64);
        Assert.LessOrEqual(UnityEngine.Mathf.Abs(mid - PianoAudioSynth.ScheduleLeadSeconds), 8 * Ms);
        Assert.Greater(PianoKeyAnimator.ReleaseSeconds, loud, "回弹比按下慢（弹簧与键自重）");
    }
}

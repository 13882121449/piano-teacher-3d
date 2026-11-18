using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PianoTeacher3D.MIDI;
using PianoTeacher3D.Piano;

namespace PianoTeacher3D.Tests.PlayMode
{
    /// <summary>
    /// 琴键键程与起音对齐（2026-11-18 P5）：在真实帧循环里测量键到底与回弹所用时间。
    /// <para>
    /// 改造前琴键用指数逼近（Slerp ×20/s），60 fps 下压到 95% 约需 140 ms，声音却在当帧就响。
    /// 现在键程按力度 16～32 ms，误差只允许一帧的量化（按本机实际 deltaTime 计）。
    /// </para>
    /// </summary>
    public class KeyTravelSyncTests
    {
        private GameObject _root;
        private PianoKeyAnimator _animator;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("KeyTravelSync");
            var keyMap = _root.AddComponent<PianoKeyMap>();
            keyMap.BuildKeys();
            _animator = _root.AddComponent<PianoKeyAnimator>();
            _animator.Initialize(keyMap);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Press_ReachesKeyBedWithinKeyTravelTime()
        {
            foreach (int velocity in new[] { 30, 64, 120 })
            {
                var note = new MidiNoteEvent(60, 0f, 1f, velocity, 0, 0);
                _animator.OnNoteOn(note);
                float elapsed = 0f, maxDelta = 0f;
                for (int frame = 0; frame < 600 && _animator.GetKeyAngle(60) < _animator.PressAngle * 0.95f; frame++)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    maxDelta = Mathf.Max(maxDelta, Time.deltaTime);
                }
                float budget = PianoKeyAnimator.PressSeconds(velocity) + maxDelta;
                Assert.GreaterOrEqual(_animator.GetKeyAngle(60), _animator.PressAngle * 0.95f, $"力度 {velocity} 未按到底");
                Assert.LessOrEqual(elapsed, budget + 1e-4f,
                    $"力度 {velocity}：键到底用时 {elapsed * 1000f:0.0} ms，预算 {budget * 1000f:0.0} ms");

                _animator.OnNoteOff(note);
                elapsed = 0f;
                maxDelta = 0f;
                for (int frame = 0; frame < 600 && _animator.GetKeyAngle(60) > 0.05f; frame++)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    maxDelta = Mathf.Max(maxDelta, Time.deltaTime);
                }
                Assert.LessOrEqual(elapsed, PianoKeyAnimator.ReleaseSeconds + maxDelta + 1e-4f,
                    $"力度 {velocity}：回弹用时 {elapsed * 1000f:0.0} ms");
            }
        }

        [UnityTest]
        public IEnumerator RetriggerDuringRelease_PressesFromCurrentDepth()
        {
            var note = new MidiNoteEvent(62, 0f, 1f, 80, 0, 0);
            _animator.OnNoteOn(note);
            for (int i = 0; i < 30 && _animator.GetKeyAngle(62) < _animator.PressAngle; i++) yield return null;
            _animator.OnNoteOff(note);
            yield return null;
            float mid = _animator.GetKeyAngle(62);
            _animator.OnNoteOn(note);
            yield return null;
            Assert.GreaterOrEqual(_animator.GetKeyAngle(62), mid - 1e-4f, "同音连击：从当前深度继续按下，不先弹回顶部");
        }
    }
}

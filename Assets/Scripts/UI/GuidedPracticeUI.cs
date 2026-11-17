using System;
using System.Collections.Generic;
using PianoTeacher3D.Core;
using PianoTeacher3D.MIDI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    public sealed class GuidedPracticeUI : MonoBehaviour
    {
        internal const string ProgressKey = "piano.firstLesson.progress";
        private const int Bpm = 60;
        private static readonly int[] Pitches = { 60, 62, 64, 65, 67 };
        private static readonly KeyCode[] Keys = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G };
        private static readonly Color Ink = new(0.038f, 0.075f, 0.16f, 0.96f);
        private static readonly Color Mint = new(0.58f, 0.92f, 0.98f);
        private static readonly Color Muted = new(0.69f, 0.76f, 0.89f);
        private static readonly Color White = new(0.96f, 0.98f, 1f);
        private static readonly string[] PitchNames = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
        private static readonly string[] BeatLabels = { "第 1 拍", "第 2 拍", "第 3 拍", "第 4 拍" };
        private static readonly string[] CountInLabels = { "预备 1", "预备 2", "预备 3", "预备 4" };
        private static double Now => WindowsMidiInput.NowSeconds;

        private readonly FiveFingerPractice _practice = new();
        private readonly WindowsMidiInput _midi = new();
        private readonly double[] _releaseAt = new double[128];
        private readonly Text[] _scoreNotes = new Text[8];
        private readonly Button[] _pianoButtons = new Button[5];
        private LessonSessionController _session;
        private Text _prompt, _feedback, _beat, _progress, _deviceStatus;
        private Button _retry;
        private RectTransform _devicePanel;
        private CanvasGroup _score;
        private int _readResults, _beatIndex, _demoIndex = -1, _retryMeasure, _saved, _cue = -1;
        private double _demoStart, _runStart;
        private bool _evaluate;

        public void Initialize(LessonSessionController session)
        {
            _session = session;
            _saved = Mathf.Clamp(PlayerPrefs.GetInt(ProgressKey, 0), 0, 4);
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "GuidedClassroom", 20);
            var top = UITheme.CreateRect(canvas.transform, "GuidedHeader", Vector2.zero, Vector2.zero, Ink);
            UITheme.StretchTop(top, 0, 0, 0, 74);
            ActionButton(top, "GuidedBack", "返回首页", 104, 20, 17,
                () => SceneManager.LoadScene("MainMenu"));
            Label(top, "GuidedTitle", "右手五指与四拍", 27, White, 140, 10, 280, 48);
            _progress = Label(top, "GuidedProgress", "", 16, Mint, 430, 17, 225, 38);
            var device = UIKit.SecondaryButton(top, "MIDI 设备", new Vector2(108, 38), ToggleDevices);
            UITheme.PlaceTopRight(device.GetComponent<RectTransform>(), 20, 17, 108, 38);

            var panel = UITheme.CreatePanel(canvas.transform, "GuidedPanel", Vector2.zero,
                new Vector2(654, 300), Ink, 20);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 24f);
            ActionButton(panel, "GuidedDemo", "听示范", 88, 21, 16, StartDemo);
            ActionButton(panel, "GuidedStartWait", "等音练习", 106, 120, 16,
                () => StartPractice(PracticeMode.WaitForCorrect));
            ActionButton(panel, "GuidedStartBeat", "跟节拍", 93, 236, 16,
                () => StartPractice(PracticeMode.KeepTime));
            ActionButton(panel, "GuidedStartIndependent", "独立弹奏", 106, 339, 16,
                () => StartPractice(PracticeMode.Independent));
            _retry = ActionButton(panel, "GuidedRetry", "重练小节", 106, 455, 16, Retry);
            _retry.gameObject.SetActive(false);
            _beat = Label(panel, "GuidedBeat", "", 15, Mint, 566, 16, 70, 38);
            _prompt = Label(panel, "GuidedPrompt", "拇指放在中央 C（C4），其余四指依次放到 D–G。", 20, White, 22, 65, 610, 34);
            _feedback = Label(panel, "GuidedFeedback", "先听两小节，再从「等音练习」开始。", 16, Muted, 22, 103, 610, 27);
            var score = UITheme.CreateRect(panel, "GuidedScore", Vector2.zero, Vector2.zero, UITheme.Transparent);
            UITheme.PlaceTopLeft(score, 21, 144, 612, 57);
            _score = score.gameObject.AddComponent<CanvasGroup>();
            for (int i = 0; i < FiveFingerPractice.Notes.Count; i++)
            {
                var note = FiveFingerPractice.Notes[i];
                float x = i * 75 + (i >= 4 ? 10 : 0);
                var tile = UIKit.Panel(score, "练习音符", new Color(0.13f, 0.21f, 0.37f), 8);
                UITheme.PlaceTopLeft(tile, x, 0, 67, 56);
                _scoreNotes[i] = Label(tile, "指法", $"{NameOf(note.Pitch)}  ·  {note.Finger}指",
                    14, White, 4, 9, 59, 36, TextAnchor.MiddleCenter);
            }
            for (int i = 0; i < Pitches.Length; i++)
            {
                int pitch = Pitches[i];
                var button = ActionButton(panel, "GuidedKey_" + pitch, $"{NameOf(pitch)}   {Keys[i]}", 112,
                    22 + i * 123, 221, () => PlayTap(pitch));
                var rect = button.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(112, 58);
                button.targetGraphic.color = White;
                UITheme.GetLabel(button).color = Ink;
                _pianoButtons[i] = button;
            }
            UpdateProgress();
            if (_saved > 0 && _saved < 4)
                _feedback.text = _saved == 1 ? "上次已听完示范，接着做等音练习。"
                    : _saved == 2 ? "接着跟节拍弹这两小节。" : "可以关掉提示，试一次独立弹奏。";
        }

        private void Update()
        {
            if (_session == null) return;
            double now = Now;
            for (int pitch = 21; pitch <= 108; pitch++)
            {
                if (_releaseAt[pitch] == 0 || now < _releaseAt[pitch]) continue;
                _session.ReleaseGuidedKey(pitch);
                _releaseAt[pitch] = 0;
            }
            if (_demoIndex >= 0) UpdateDemo(now);
            if (_devicePanel == null || !_devicePanel.gameObject.activeSelf)
                for (int i = 0; i < Keys.Length; i++)
                    if (Input.GetKeyDown(Keys[i])) PlayTap(Pitches[i]);
            while (_midi.TryRead(out var note))
            {
                if (note.Pitch < 21 || note.Pitch > 108 || !Application.isFocused) continue;
                if (note.IsNoteOn) PlayInput(note.Pitch, note.Velocity, note.TimestampSeconds);
                else
                {
                    _releaseAt[note.Pitch] = 0;
                    _session.ReleaseGuidedKey(note.Pitch);
                }
            }
            if (!_evaluate || _practice.IsComplete) return;
            if (_practice.Mode != PracticeMode.WaitForCorrect)
            {
                int beat = (int)Math.Floor((now - _runStart) / _practice.BeatSeconds);
                if (beat >= 0 && beat != _beatIndex)
                {
                    _beatIndex = beat;
                    _session.PlayGuidedBeat(beat % 4);
                    _beat.text = beat < 4 ? CountInLabels[beat] : BeatLabels[beat % 4];
                    UpdatePrompt();
                }
            }
            _practice.Advance(now);
            ReadResults();
        }

        private void StartDemo()
        {
            ResetSound();
            _evaluate = false;
            _demoIndex = 0;
            _demoStart = Now;
            _feedback.text = "听四拍之间的间隔，观察亮键和指号。";
            SetScoreVisible(true);
            UpdateDemo(_demoStart);
        }

        private void UpdateDemo(double now)
        {
            if (now < _demoStart + _demoIndex) return;
            if (_demoIndex >= FiveFingerPractice.Notes.Count)
            {
                _demoIndex = -1;
                _beat.text = "";
                _prompt.text = "示范结束，点击「等音练习」。";
                RefreshCue(-1, -1);
                SaveProgress(1);
                return;
            }
            var note = FiveFingerPractice.Notes[_demoIndex];
            _session.PlayGuidedBeat(note.Beat - 1);
            PlaySound(note.Pitch, 84, 0.55);
            _beat.text = $"第 {note.Beat} 拍";
            _prompt.text = $"第 {note.Measure} 小节 · {NameOf(note.Pitch)} · 右手 {note.Finger} 指";
            RefreshCue(note.Pitch, _demoIndex++);
        }

        private void StartPractice(PracticeMode mode, int measure = 0)
        {
            ResetSound();
            _demoIndex = -1;
            _evaluate = true;
            _runStart = Now;
            _practice.Start(mode, _runStart, Bpm, measure);
            _readResults = 0;
            _beatIndex = -1;
            _retryMeasure = 0;
            _retry.gameObject.SetActive(false);
            SetScoreVisible(mode != PracticeMode.Independent);
            _beat.text = mode == PracticeMode.WaitForCorrect ? "" : "预备";
            _feedback.text = mode == PracticeMode.WaitForCorrect ? "弹对后进入下一音。" : "听四拍预备，下一小节开始弹奏。";
            UpdatePrompt();
        }

        private void PlayTap(int pitch)
        {
            double now = Now;
            PlayInput(pitch, 85, now);
            _releaseAt[pitch] = now + 0.22;
        }

        private void PlayInput(int pitch, int velocity, double timestamp)
        {
            if (_demoIndex >= 0) return;
            PlaySound(pitch, velocity, 0);
            if (!_evaluate || _practice.IsComplete || timestamp < _runStart) return;
            _practice.PlayNote(pitch, timestamp);
            ReadResults();
        }

        private void PlaySound(int pitch, int velocity, double length)
        {
            // 重按同音先释放上一次发声，避免快速重复时残留按下状态。
            _session.ReleaseGuidedKey(pitch);
            _session.PlayGuidedKey(pitch, velocity);
            _releaseAt[pitch] = length > 0 ? Now + length : 0;
        }

        private void ReadResults()
        {
            if (_readResults == _practice.Results.Count) return;
            var result = _practice.Results[_practice.Results.Count - 1];
            _readResults = _practice.Results.Count;
            if (_practice.IsComplete)
            {
                ShowSummary();
                return;
            }
            if (_practice.Mode != PracticeMode.Independent) _feedback.text = Describe(result);
            UpdatePrompt();
        }

        private void ShowSummary()
        {
            int correct = 0, wrong = 0, missed = 0, early = 0, late = 0;
            PracticeFeedback? firstProblem = null;
            foreach (var result in _practice.Results)
            {
                switch (result.Kind)
                {
                    case PracticeFeedbackKind.Correct: correct++; continue;
                    case PracticeFeedbackKind.WrongPitch:
                    case PracticeFeedbackKind.Extra: wrong++; break;
                    case PracticeFeedbackKind.Missed: missed++; break;
                    case PracticeFeedbackKind.Early: early++; break;
                    case PracticeFeedbackKind.Late: late++; break;
                    default: continue;
                }
                firstProblem ??= result;
            }
            _prompt.text = $"本轮结束 · 拍准 {correct} · 错音 {wrong} · 漏音 {missed} · 早 {early} · 晚 {late}";
            _beat.text = "";
            _feedback.text = firstProblem.HasValue ? Describe(firstProblem.Value) : "本轮全部正确。";
            _retryMeasure = firstProblem.HasValue ? FiveFingerPractice.Notes[firstProblem.Value.NoteIndex].Measure : 0;
            _retry.gameObject.SetActive(_retryMeasure > 0);
            if (_retryMeasure > 0) UITheme.SetButtonLabel(_retry, $"重练第 {_retryMeasure} 小节");
            RefreshCue(-1, -1);
            if (_practice.SelectedMeasure != 0) return;
            if (_practice.Mode == PracticeMode.WaitForCorrect) SaveProgress(2);
            if (_practice.Mode == PracticeMode.KeepTime && missed + wrong + early + late == 0) SaveProgress(3);
            if (_practice.Mode == PracticeMode.Independent && missed + wrong + early + late == 0) SaveProgress(4);
        }

        private void Retry() => StartPractice(
            _practice.Mode == PracticeMode.WaitForCorrect ? PracticeMode.WaitForCorrect : PracticeMode.KeepTime,
            _retryMeasure);

        private void UpdatePrompt()
        {
            int index = _practice.CurrentNoteIndex;
            if (!_practice.ShowsPrompt || index < 0)
            {
                _prompt.text = "独立弹奏 · 听节拍，凭记忆弹完两小节。";
                RefreshCue(-1, -1);
                return;
            }
            var note = FiveFingerPractice.Notes[index];
            _prompt.text = $"第 {note.Measure} 小节 · 第 {note.Beat} 拍 · {NameOf(note.Pitch)} · 右手 {note.Finger} 指";
            RefreshCue(note.Pitch, index);
        }

        private void RefreshCue(int pitch, int scoreIndex)
        {
            if (pitch != _cue)
            {
                _cue = pitch;
                _session.SetGuidedTarget(pitch);
            }
            for (int i = 0; i < _scoreNotes.Length; i++) _scoreNotes[i].color = i == scoreIndex ? Mint : White;
            for (int i = 0; i < _pianoButtons.Length; i++)
                _pianoButtons[i].targetGraphic.color = Pitches[i] == pitch ? Mint : White;
        }

        private static string Describe(PracticeFeedback result)
        {
            var note = FiveFingerPractice.Notes[result.NoteIndex];
            string where = $"第 {note.Measure} 小节第 {note.Beat} 拍";
            string expected = NameOf(result.ExpectedPitch);
            return result.Kind switch
            {
                PracticeFeedbackKind.Correct => $"{expected} 正确",
                PracticeFeedbackKind.WrongPitch => $"{where}，{expected} 弹成了 {NameOf(result.ActualPitch)}",
                PracticeFeedbackKind.Extra => $"{where}附近，多弹了 {NameOf(result.ActualPitch)}",
                PracticeFeedbackKind.Missed => $"{where}，漏弹 {expected}",
                PracticeFeedbackKind.Early => $"{where}，{expected} 早了 {Math.Abs(result.OffsetSeconds) * 1000:0} 毫秒",
                PracticeFeedbackKind.Late => $"{where}，{expected} 晚了 {result.OffsetSeconds * 1000:0} 毫秒",
                _ => ""
            };
        }

        private static string NameOf(int pitch)
        {
            return pitch >= 0 && pitch <= 127 ? PitchNames[pitch % 12] + (pitch / 12 - 1) : "";
        }

        private void SetScoreVisible(bool visible)
        {
            _score.alpha = visible ? 1 : 0;
            _score.blocksRaycasts = visible;
        }

        private void SaveProgress(int value)
        {
            if (value <= _saved) return;
            _saved = value;
            if (!Application.isBatchMode)
            {
                PlayerPrefs.SetInt(ProgressKey, value);
                PlayerPrefs.Save();
            }
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            string[] labels = { "从示范开始", "下一步 · 等音练习", "下一步 · 跟节拍", "下一步 · 独立弹奏", "已通过第一课" };
            _progress.text = labels[_saved];
        }

        private void ResetSound()
        {
            Array.Clear(_releaseAt, 0, _releaseAt.Length);
            _session.StopGuidedKeys();
            _cue = -1;
        }

        private void ToggleDevices()
        {
            if (_devicePanel != null && _devicePanel.gameObject.activeSelf)
            {
                _devicePanel.gameObject.SetActive(false);
                return;
            }
            if (_devicePanel != null) Destroy(_devicePanel.gameObject);
            var canvas = _prompt.canvas.rootCanvas;
            var devices = WindowsMidiInput.GetDevices();
            int rows = Mathf.Min(16, devices.Count);
            float height = 116 + 34 * rows;
            _devicePanel = UITheme.CreatePanel(canvas.transform, "GuidedMidiPanel", Vector2.zero,
                new Vector2(334, height), Ink, 14);
            UITheme.PlaceTopRight(_devicePanel, 20, 78, 334, height);
            _deviceStatus = Label(_devicePanel, "GuidedDeviceStatus",
                _midi.IsConnected ? "MIDI 已连接" : rows == 0 ? "未检测到 MIDI 输入设备" : "选择要连接的输入设备",
                16, White, 12, 10, 310, 29);
            for (int i = 0; i < rows; i++)
            {
                var device = devices[i];
                ActionButton(_devicePanel, "GuidedMidi_" + device.Id, UITheme.Truncate(device.Name, 29), 310, 12,
                    43 + i * 34, () =>
                    {
                        ResetSound();
                        _deviceStatus.text = _midi.TryConnect(device.Id, out var error) ? "已连接 " + device.Name : error;
                    }).GetComponent<RectTransform>().sizeDelta = new Vector2(310, 31);
            }
            ActionButton(_devicePanel, "GuidedMidiDisconnect", "断开", 85, 12, height - 50, () =>
            {
                _midi.Disconnect();
                ResetSound();
                _deviceStatus.text = "MIDI 已断开";
            });
            ActionButton(_devicePanel, "GuidedMidiClose", "关闭", 85, 109, height - 50, ToggleDevices);
        }

        private static Button ActionButton(RectTransform parent, string name, string text, float width, float x, float y,
            Action click)
        {
            var button = UIKit.SecondaryButton(parent, text, new Vector2(width, 38), () => click());
            button.gameObject.name = name;
            UITheme.PlaceTopLeft(button.GetComponent<RectTransform>(), x, y, width, 38);
            return button;
        }

        private static Text Label(RectTransform parent, string name, string text, int size, Color color,
            float x, float y, float width, float height, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var label = UIKit.Label(parent, text, size, color, anchor, FontStyle.Normal, name);
            UITheme.PlaceTopLeft(label.rectTransform, x, y, width, height);
            return label;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _session != null) ResetSound();
        }

        private void OnDestroy() => _midi.Dispose();
    }
}

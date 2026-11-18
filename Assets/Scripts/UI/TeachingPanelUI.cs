using System.Collections.Generic;
using PianoTeacher3D.CameraControl;
using PianoTeacher3D.Core;
using PianoTeacher3D.Teaching;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 课程界面的教学工具（2026-11-18）：底栏「教学」胶囊打开一块毛玻璃面板，集中放
    /// 下落音符 / 五线谱 / 音名 / 等待模式 / 电脑键盘弹奏 / A-B 循环 / 渐进提速 / 手型要点；
    /// 另外负责顶部成绩条、判定飘字、难点提示条、一遍结束的成绩卡与手型要点卡。
    /// </summary>
    public sealed class TeachingPanelUI : MonoBehaviour
    {
        private const float PanelWidth = 452f;
        private const float PanelHeight = 214f;
        private const float ChipW = 136f;

        private LessonSessionController _session;
        private TeachingDirector _teaching;
        private RectTransform _canvas;
        private RectTransform _panel;
        private Button _fallingChip, _staffChip, _namesChip, _waitChip, _keyboardChip, _abChip, _rampChip, _postureBtn;
        private Text _midiText;
        private StaffView _staff;
        private RectTransform _hud;
        private Text _hudText;
        private RectTransform _tip;
        private Text _tipText;
        private float _tipUntil;
        private RectTransform _result;
        private Text _resultText;
        private float _resultUntil;
        private RectTransform _posture;
        private readonly List<(Text text, float born)> _popups = new List<(Text, float)>();
        private int _popupNext;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;
        public RectTransform Panel => _panel;
        public StaffView Staff => _staff;

        public static TeachingPanelUI Create(LessonSessionController session, RectTransform canvas, RectTransform playerBar)
        {
            var host = canvas.gameObject.AddComponent<TeachingPanelUI>();
            host.Build(session, canvas, playerBar);
            return host;
        }

        private void Build(LessonSessionController session, RectTransform canvas, RectTransform playerBar)
        {
            _session = session;
            _teaching = session != null ? session.Teaching : null;
            _canvas = canvas;

            _panel = UITheme.CreatePanel(playerBar, "TeachingPanel", Vector2.zero, new Vector2(PanelWidth, PanelHeight),
                UITheme.PanelBgRaised, UITheme.RadiusMedium);
            var title = UIKit.Label(_panel, "教学工具", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, "TeachTitle");
            UITheme.SetWeight(title, true);
            UITheme.PlaceTopLeft(title.rectTransform, 16f, 12f, 200f, 20f);
            _midiText = UIKit.Label(_panel, "", UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleRight, FontStyle.Normal, "MidiState");
            UITheme.PlaceTopRight(_midiText.rectTransform, 16f, 12f, 220f, 20f);

            _fallingChip = Chip("FallingChip", 0, 0, () => _teaching?.SetFallingNotes(!_teaching.FallingNotes), "下落音符：音符从琴键后方落下，落到键面时按下");
            _staffChip = Chip("StaffChip", 1, 0, () => _teaching?.SetStaffVisible(!_teaching.StaffVisible), "五线谱：顶部滚动的大谱表，音符过播放线时按下");
            _namesChip = Chip("NamesChip", 2, 0, () => _teaching?.CycleNoteNames(), "琴键音名：关 / C D E / do re mi");
            _waitChip = Chip("WaitChip", 0, 1, () => _teaching?.SetWaitMode(!_teaching.WaitMode), "等待模式：弹对了才继续（接 MIDI 键盘或开启电脑键盘）");
            _keyboardChip = Chip("KeyboardChip", 1, 1, () => _teaching?.SetKeyboardPlay(!_teaching.KeyboardPlay), "电脑键盘弹奏：Z–M / Q–I 两排，Shift 降一个八度");
            _rampChip = Chip("RampChip", 2, 1, () => _teaching?.SetSpeedRamp(!_teaching.SpeedRamp), "渐进提速：A-B 循环从 60% 起，每遍加快 5%，直到原速");
            _abChip = Chip("ABChip", 0, 2, () => _teaching?.StepABLoop(), "A-B 循环：点一次记 A、再点记 B 开始循环，再点清除");
            _postureBtn = Chip("PostureChip", 1, 2, () => ShowPosture(true), "手型要点：看角色特写，对照正确手型");

            var hint = UIKit.Label(_panel, "电脑键盘：Z X C V B N M = C4–B4，Q–I = C5–C6；成绩按 ±80 ms 判准确",
                UITheme.FontCaption, UITheme.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal, "TeachHint");
            UITheme.Place(hint.rectTransform, 16f, 12f, PanelWidth - 32f, 18f);
            UIAppear.Attach(_panel.gameObject, 0f, 0.92f);
            _panel.gameObject.SetActive(false);

            _staff = new GameObject("StaffView").AddComponent<StaffView>();
            _staff.transform.SetParent(transform, false);
            _staff.Build(_canvas);
            _staff.Panel.gameObject.SetActive(false);

            BuildHud();
            BuildTip();
            BuildResult();
            BuildPosture();
            BuildPopups();

            if (_teaching != null)
            {
                _teaching.SettingsChanged += Refresh;
                _teaching.Judged += OnJudged;
                _teaching.TipRaised += OnTip;
                _teaching.RunFinished += OnRunFinished;
            }
            Refresh();
        }

        /// <summary>与「更多」浮层同一位置：底栏上方右对齐。</summary>
        public void Layout(float rightPad, float bottom)
        {
            if (_panel != null) UITheme.PlaceRight(_panel, rightPad, bottom, PanelWidth, PanelHeight);
        }

        public void SetOpen(bool open)
        {
            if (_panel == null || open == IsOpen) return;
            if (open)
            {
                Refresh();
                _panel.gameObject.SetActive(true);
            }
            else UIAppear.Hide(_panel.gameObject);
        }

        private Button Chip(string name, int col, int row, UnityEngine.Events.UnityAction onClick, string tooltip)
        {
            var chip = UIKit.Chip(_panel, name, ChipW, onClick, tooltip);
            UITheme.PlaceTopLeft(chip.GetComponent<RectTransform>(), 16f + col * (ChipW + 8f), 44f + row * 44f,
                ChipW, UITheme.Layout.ChipHeight);
            return chip;
        }

        private void Refresh()
        {
            if (_teaching == null) return;
            bool song = _session != null && _session.HasSong;
            UIKit.SetChip(_fallingChip, "下落音符", _teaching.FallingNotes ? "开" : "关", _teaching.FallingNotes, true);
            UIKit.SetChip(_staffChip, "五线谱", _teaching.StaffVisible ? "开" : "关", _teaching.StaffVisible, true);
            string names = _teaching.NoteNames switch { NoteNameStyle.Letters => "C D E", NoteNameStyle.Solfege => "do re mi", _ => "关" };
            UIKit.SetChip(_namesChip, "音名", names, _teaching.NoteNames != NoteNameStyle.Off, true);
            UIKit.SetChip(_waitChip, "等待模式", _teaching.WaitMode ? "开" : "关", _teaching.WaitMode, song);
            UIKit.SetChip(_keyboardChip, "电脑键盘", _teaching.KeyboardPlay ? "开" : "关", _teaching.KeyboardPlay, true);
            UIKit.SetChip(_rampChip, "渐进提速", _teaching.SpeedRamp ? "开" : "关", _teaching.SpeedRamp, song);
            string ab = _teaching.HasABLoop ? "清除" : (_teaching.LoopA.HasValue ? "设 B" : "设 A");
            UIKit.SetChip(_abChip, "A-B 循环", ab, _teaching.HasABLoop || _teaching.LoopA.HasValue, song);
            UIKit.SetChip(_postureBtn, "手型要点", "", false, true);
            _staff.SetVisible(_teaching.StaffVisible && song);
            _staff.SetNotes(_teaching.PracticeNotes, _teaching.Fingering);
        }

        private void Update()
        {
            if (_teaching == null) return;
            float now = _session != null ? _session.CurrentTime : 0f;
            if (_staff != null && _teaching.StaffVisible) _staff.Refresh(now);
            RefreshMidiText();
            var scorer = _teaching.Scorer;
            bool showHud = scorer.InputCount > 0 || _teaching.WaitMode;
            if (_hud.gameObject.activeSelf != showHud) _hud.gameObject.SetActive(showHud);
            if (showHud)
            {
                RefreshHudText(scorer);
                float top = UITheme.Layout.ScreenMargin + UITheme.Layout.StatusBarHeight + UITheme.Space12 +
                            (_teaching.StaffVisible && _staff.Panel.gameObject.activeSelf ? 176f + UITheme.Space8 : 0f);
                _hud.anchoredPosition = new Vector2(0f, -top);
            }
            if (_tip.gameObject.activeSelf && Time.unscaledTime > _tipUntil) UIAppear.Hide(_tip.gameObject);
            if (_result.gameObject.activeSelf && Time.unscaledTime > _resultUntil) UIAppear.Hide(_result.gameObject);
            UpdatePopups();
        }

        // ==================== 成绩条 / 提示 / 成绩卡 ====================

        private string _lastDevice;
        private int _lastAccuracy = -1, _lastStreak = -1, _lastBest = -1;
        private bool _lastHolding;

        /// <summary>设备名变化时才重建文字（Update 热路径零分配）。</summary>
        private void RefreshMidiText()
        {
            string device = _teaching.MidiDevice;
            if (device == _lastDevice) return;
            _lastDevice = device;
            _midiText.text = string.IsNullOrEmpty(device) ? "MIDI 键盘：未连接" : "MIDI：" + device;
        }

        /// <summary>成绩变化时才重建成绩条文字。</summary>
        private void RefreshHudText(PracticeScorer scorer)
        {
            int accuracy = Mathf.RoundToInt(scorer.Accuracy * 100f);
            if (accuracy == _lastAccuracy && scorer.Streak == _lastStreak && scorer.BestStreak == _lastBest &&
                scorer.IsHolding == _lastHolding) return;
            _lastAccuracy = accuracy;
            _lastStreak = scorer.Streak;
            _lastBest = scorer.BestStreak;
            _lastHolding = scorer.IsHolding;
            string wait = scorer.IsHolding ? "  ·  等你弹" : string.Empty;
            _hudText.text = $"准确 {accuracy}%  ·  连击 {scorer.Streak}  ·  最佳 {scorer.BestStreak}{wait}";
        }

        private void BuildHud()
        {
            _hud = UITheme.CreatePanel(_canvas, "ScoreHud", Vector2.zero, new Vector2(360f, 34f), UITheme.PanelBgGlass, UITheme.RadiusLarge);
            _hud.GetComponent<Image>().raycastTarget = false;
            _hud.anchorMin = _hud.anchorMax = new Vector2(0.5f, 1f);
            _hud.pivot = new Vector2(0.5f, 1f);
            _hudText = UIKit.Label(_hud, "", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal, "ScoreText");
            UITheme.StretchAll(_hudText.rectTransform, 12f, 12f, 0f, 0f);
            UIAppear.Attach(_hud.gameObject, 0f, 0.95f);
            _hud.gameObject.SetActive(false);
        }

        private void BuildTip()
        {
            _tip = UITheme.CreatePanel(_canvas, "CoachTip", Vector2.zero, new Vector2(460f, 40f), UITheme.PanelBgGlass, UITheme.RadiusLarge);
            _tip.GetComponent<Image>().raycastTarget = false;
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0f);
            _tip.pivot = new Vector2(0.5f, 0f);
            _tip.anchoredPosition = new Vector2(0f, 150f);
            _tipText = UIKit.Label(_tip, "", UITheme.FontBody, UITheme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal, "TipText");
            UITheme.StretchAll(_tipText.rectTransform, 14f, 14f, 0f, 0f);
            UIAppear.Attach(_tip.gameObject, 0f, 0.94f, new Vector2(0f, -12f));
            _tip.gameObject.SetActive(false);
        }

        private void BuildResult()
        {
            _result = UITheme.CreatePanel(_canvas, "RunResult", Vector2.zero, new Vector2(360f, 96f), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            _result.GetComponent<Image>().raycastTarget = false;
            UITheme.PlaceCenter(_result, 360f, 96f);
            _resultText = UIKit.Label(_result, "", UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal, "ResultText");
            UITheme.StretchAll(_resultText.rectTransform, 12f, 12f, 8f, 8f);
            _resultText.lineSpacing = 1.15f;
            UIAppear.Attach(_result.gameObject, 0f, 0.88f);
            _result.gameObject.SetActive(false);
        }

        private void OnTip(TeachingCoach.Tip tip)
        {
            _tipText.text = TeachingCoach.TextFor(tip);
            _tipUntil = Time.unscaledTime + 4f;
            if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
        }

        private void OnRunFinished(TeachingDirector.RunResult r)
        {
            string stars = new string('★', r.Stars) + new string('☆', 3 - r.Stars);
            string record = r.Improved ? "  新纪录！" : string.Empty;
            _resultText.text = $"本遍准确 {r.Accuracy * 100f:0}%  {stars}{record}\n<size=13>最佳连击 {r.BestStreak}  ·  速度 {r.Speed:0.##}×</size>";
            _resultText.supportRichText = true;
            _resultUntil = Time.unscaledTime + 4f;
            if (!_result.gameObject.activeSelf) _result.gameObject.SetActive(true);
        }

        // ==================== 判定飘字 ====================

        private void BuildPopups()
        {
            for (int i = 0; i < 8; i++)
            {
                var t = UIKit.Label(_canvas, "", UITheme.FontTitle, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Judgement");
                UITheme.SetWeight(t, true);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(140f, 32f);
                t.raycastTarget = false;
                var outline = t.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
                t.gameObject.SetActive(false);
                _popups.Add((t, -10f));
            }
        }

        private void OnJudged(Judgement j, int pitch, float delta)
        {
            if (_popups.Count == 0) return;
            var (text, _) = _popups[_popupNext];
            text.text = j switch
            {
                Judgement.Perfect => "准确",
                Judgement.Early => "偏早",
                Judgement.Late => "偏晚",
                Judgement.Miss => "漏弹",
                _ => "弹错"
            };
            text.color = j switch
            {
                Judgement.Perfect => new Color(0.45f, 0.95f, 1f),
                Judgement.Early or Judgement.Late => UITheme.Warning,
                _ => UITheme.Error
            };
            // 横向位置按音高散开（低音在左），避免飘字叠在一起
            float x = Mathf.Lerp(-260f, 260f, Mathf.InverseLerp(36f, 96f, pitch));
            text.rectTransform.anchoredPosition = new Vector2(x, 200f);
            text.gameObject.SetActive(true);
            _popups[_popupNext] = (text, Time.unscaledTime);
            _popupNext = (_popupNext + 1) % _popups.Count;
        }

        private void UpdatePopups()
        {
            for (int i = 0; i < _popups.Count; i++)
            {
                var (text, born) = _popups[i];
                if (!text.gameObject.activeSelf) continue;
                float t = (Time.unscaledTime - born) / 0.8f;
                if (t >= 1f)
                {
                    text.gameObject.SetActive(false);
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                var p = text.rectTransform.anchoredPosition;
                text.rectTransform.anchoredPosition = new Vector2(p.x, 200f + ease * 36f);
                var c = text.color;
                c.a = 1f - t * t;
                text.color = c;
                float s = UIMotion.Reduced ? 1f : 1f + 0.25f * (1f - ease) * (1f - ease);
                text.rectTransform.localScale = new Vector3(s, s, 1f);
            }
        }

        // ==================== 手型要点 ====================

        private void BuildPosture()
        {
            _posture = UITheme.CreatePanel(_canvas, "PostureCard", Vector2.zero, new Vector2(420f, 236f), UITheme.PanelBgRaised, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_posture, 420f, 236f);
            var title = UIKit.Label(_posture, "手型要点", UITheme.FontTitle, UITheme.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold, "PostureTitle");
            UITheme.SetWeight(title, true);
            UITheme.PlaceTopLeft(title.rectTransform, 20f, 16f, 200f, 26f);
            for (int i = 0; i < TeachingCoach.PostureTips.Length; i++)
            {
                var line = UIKit.Label(_posture, (i + 1) + ".  " + TeachingCoach.PostureTips[i], UITheme.FontBody, UITheme.TextSecondary,
                    TextAnchor.MiddleLeft, FontStyle.Normal, "PostureTip");
                UITheme.PlaceTopLeft(line.rectTransform, 20f, 52f + i * 30f, 380f, 26f);
            }
            var demo = UIKit.TextButton(_posture, "看演示", 96f, 32f, () =>
            {
                // 特写看手型，放慢到 0.75× 方便观察
                _session?.SetCameraMode(CameraMode.Closeup);
                _session?.SetSpeed(0.75f);
                if (_session != null && _session.HasSong && _session.PlaybackState != PlaybackState.Playing) _session.Play();
                ShowPosture(false);
            }, UITheme.FontBodySmall, "切到手部特写并放慢到 0.75×");
            UITheme.Place(demo.GetComponent<RectTransform>(), 20f, 16f, 96f, 32f);
            var close = UIKit.TextButton(_posture, "关闭", 72f, 32f, () => ShowPosture(false), UITheme.FontBodySmall);
            UITheme.PlaceRight(close.GetComponent<RectTransform>(), 20f, 16f, 72f, 32f);
            UIAppear.Attach(_posture.gameObject, 0f, 0.92f);
            _posture.gameObject.SetActive(false);
        }

        private void ShowPosture(bool show)
        {
            if (show)
            {
                SetOpen(false);
                _posture.gameObject.SetActive(true);
            }
            else UIAppear.Hide(_posture.gameObject);
        }

        private void OnDestroy()
        {
            if (_teaching == null) return;
            _teaching.SettingsChanged -= Refresh;
            _teaching.Judged -= OnJudged;
            _teaching.TipRaised -= OnTip;
            _teaching.RunFinished -= OnRunFinished;
        }
    }
}

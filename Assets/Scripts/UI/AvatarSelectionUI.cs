using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>
    /// 角色选择 UI（方案 3.2 / 4.2）：顶部右侧 3 个预设形象按钮，
    /// 点击切换并高亮当前项（暖金激活态）。切换只改表现层，不影响时间轴。
    /// </summary>
    public class AvatarSelectionUI : MonoBehaviour
    {
        private static readonly string[] AvatarNames = { "写实女性", "写实男性", "卡通角色" };

        private Core.LessonSessionController _session;
        private readonly List<Button> _buttons = new();

        public void Initialize(Core.LessonSessionController session)
        {
            _session = session;
            _session.OnAvatarChanged += OnAvatarChanged;
            BuildUI();
            Highlight(_session.CurrentAvatarIndex);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnAvatarChanged -= OnAvatarChanged;
        }

        private void OnAvatarChanged(int index) => Highlight(index);

        private void BuildUI()
        {
            UITheme.EnsureEventSystem();
            var canvas = UITheme.CreateCanvas(transform, "AvatarSelectionCanvas");

            // 顶部右侧：与左侧曲目信息栏同高
            const float y = 322f;
            const float w = 130f;
            const float step = 140f;
            float startX = 275f;

            for (int i = 0; i < AvatarNames.Length; i++)
            {
                int index = i;
                var btn = UITheme.CreateButton(canvas.transform, AvatarNames[i],
                    new Vector2(startX + index * step, y), new Vector2(w, 46f),
                    () => _session?.SwitchAvatar(index), UITheme.FontSmall + 1);
                _buttons.Add(btn);
            }
        }

        private void Highlight(int active)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                UITheme.SetButtonActive(_buttons[i], i == active);
            }
        }
    }
}

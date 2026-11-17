using System;
using System.Collections.Generic;
using PianoTeacher3D.Characters;
using PianoTeacher3D.Customization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoTeacher3D.UI
{
    /// <summary>离线标点拟合向导，照片仅由当前面板临时持有。</summary>
    public sealed class PhotoAvatarPanel : MonoBehaviour
    {
        private const int PreviewLayer = 30;
        private const float ImageBoxWidth = 360f;
        private const float ImageBoxHeight = 356f;
        private readonly List<Vector2> _marks = new List<Vector2>(PhotoAvatarFitter.LandmarkCount);
        private readonly List<GameObject> _dots = new List<GameObject>(PhotoAvatarFitter.LandmarkCount);
        private Func<PhotoAvatarData, string> _onSave;
        private GameObject _canvas;
        private RectTransform _panel;
        private Texture2D _source;
        private RawImage _photo;
        private RawImage _render;
        private Text _status;
        private Button _fitButton;
        private Button _saveButton;
        private Text _templateLabel;
        private Text _outfitLabel;
        private Text _complexionLabel;
        private InputField _name;
        private bool _shortHair;
        private bool _side;
        private int _outfit;
        private int _complexion;
        private PhotoFilePickerUI _picker;
        private GameObject _studio;
        private Camera _camera;
        private RenderTexture _target;
        private CharacterProfile _previewProfile;
        private RocketboxCharacterView _view;
        private PhotoAvatarData _draft;
        private GameObject _trigger;

        public bool IsOpen => _canvas != null && _canvas.activeSelf;

        public void Initialize(Func<PhotoAvatarData, string> onSave)
        {
            _onSave = onSave;
            Build();
            Close();
        }

        public void Show()
        {
            if (IsOpen) return;
            _trigger = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            _canvas.SetActive(true);
            _name.text = "照片演奏者";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            UpdateStatus();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_picker != null)
            {
                if (!_picker.Tick()) _picker = null;
                return;
            }
            if (UITheme.ConsumeEscape()) Close();
        }

        private void Close()
        {
            _picker?.Close();
            _picker = null;
            if (IsOpen)
            {
                _canvas.SetActive(false);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_trigger);
            }
            _trigger = null;
            ReleaseSource();
            ClearDraft();
        }

        private void OnDestroy()
        {
            _picker?.Close();
            ReleaseSource();
            ClearDraft();
        }

        private void Build()
        {
            var canvas = UITheme.CreateCanvas(transform, "PhotoAvatarCanvas", 320);
            _canvas = canvas.gameObject;
            var overlay = UITheme.CreatePanel(canvas.transform, "Background", Vector2.zero, Vector2.one,
                new Color(.025f, .039f, .065f, .96f));
            UITheme.StretchAll(overlay, 0f, 0f, 0f, 0f);
            _panel = UITheme.CreatePanel(overlay, "PhotoAvatarPanel", Vector2.zero, new Vector2(824f, 570f),
                UITheme.PanelBg, UITheme.RadiusLarge);
            UITheme.PlaceCenter(_panel, 824f, 570f);
            Label("从照片创建演奏角色", 24f, 16f, 618f, 34f, UITheme.FontTitle);
            Label("选择你有权使用的单人正脸照片。标点与生成均在本机完成。", 24f, 54f, 776f, 24f);
            PlaceButton("关闭", 726f, 18f, 72f, Close);

            var photoPad = UITheme.CreatePanel(_panel, "PhotoPad", Vector2.zero, Vector2.one, UITheme.PanelBgDeep);
            UITheme.PlaceTopLeft(photoPad, 24f, 88f, ImageBoxWidth, ImageBoxHeight);
            _photo = MakeImage(photoPad, "待标点照片");
            UITheme.PlaceCenter(_photo.rectTransform, ImageBoxWidth, ImageBoxHeight);
            var trigger = _photo.gameObject.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(data => AddMark(data as PointerEventData));
            trigger.triggers.Add(click);

            var renderPad = UITheme.CreatePanel(_panel, "ModelPad", Vector2.zero, Vector2.one, UITheme.PanelBgDeep);
            UITheme.PlaceTopLeft(renderPad, 400f, 88f, 394f, 264f);
            _render = MakeImage(renderPad, "三维人像");
            _render.raycastTarget = false;
            UITheme.PlaceCenter(_render.rectTransform, 394f, 264f);
            _render.enabled = false;
            PlaceButton("正面 / 侧面", 400f, 358f, 122f, FlipPreview);
            _templateLabel = UITheme.GetLabel(PlaceButton("样板：束发", 530f, 358f, 124f, () =>
            {
                _shortHair = !_shortHair;
                _templateLabel.text = _shortHair ? "样板：短发" : "样板：束发";
                RefitIfReady();
            }));
            _outfitLabel = UITheme.GetLabel(PlaceButton("服装：海蓝", 664f, 358f, 130f, () =>
            {
                _outfit = (_outfit + 1) % 3;
                _outfitLabel.text = "服装：" + new[] { "海蓝", "暖红", "松绿" }[_outfit];
                RefitIfReady();
            }));
            _complexionLabel = UITheme.GetLabel(PlaceButton("肤色：原样", 400f, 396f, 122f, () =>
            {
                _complexion = (_complexion + 1) % 3;
                _complexionLabel.text = "肤色：" + new[] { "原样", "深色", "浅色" }[_complexion];
                RefitIfReady();
            }));
            var nameBackground = UITheme.CreatePanel(_panel, "角色名", Vector2.zero, Vector2.one, UITheme.PanelBgDeep);
            UITheme.PlaceTopLeft(nameBackground, 530f, 396f, 264f, 32f);
            _name = nameBackground.gameObject.AddComponent<InputField>();
            _name.targetGraphic = nameBackground.GetComponent<Graphic>();
            var nameText = UITheme.CreateText(nameBackground, "", Vector2.zero, Vector2.one, UITheme.FontSmall,
                UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.StretchAll(nameText.rectTransform, 10f, 4f, 10f, 4f);
            _name.textComponent = nameText;
            _name.characterLimit = 22;
            _name.lineType = InputField.LineType.SingleLine;

            _status = Label("", 24f, 452f, 770f, 42f);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceButton("选择照片", 24f, 514f, 142f, Pick);
            PlaceButton("撤销标点", 174f, 514f, 142f, UndoMark);
            _fitButton = PlaceButton("生成三维预览", 324f, 514f, 172f, Fit);
            _saveButton = PlaceButton("保存并上台", 622f, 514f, 172f, Save);
        }

        private void Pick()
        {
            if (_picker != null) return;
            _picker = new PhotoFilePickerUI(transform, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                path =>
                {
                    _picker = null;
                    var result = PhotoInputValidator.ValidateFile(path);
                    if (!result.Ok)
                    {
                        UpdateStatus(result.Reason + " " + result.Guidance);
                        return;
                    }
                    ReleaseSource();
                    ClearDraft();
                    _source = result.Texture;
                    _photo.texture = _source;
                    float scale = Mathf.Min(ImageBoxWidth / result.Width, ImageBoxHeight / result.Height);
                    UITheme.PlaceCenter(_photo.rectTransform, result.Width * scale, result.Height * scale);
                    UpdateStatus();
                }, () => _picker = null);
        }

        private void AddMark(PointerEventData pointer)
        {
            if (_source == null || pointer == null || _marks.Count >= PhotoAvatarFitter.LandmarkCount ||
                pointer.button != PointerEventData.InputButton.Left) return;
            var rt = _photo.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, pointer.position, pointer.pressEventCamera,
                out var local) || !rt.rect.Contains(local)) return;
            var uv = new Vector2(Mathf.InverseLerp(rt.rect.xMin, rt.rect.xMax, local.x),
                Mathf.InverseLerp(rt.rect.yMax, rt.rect.yMin, local.y));
            ClearDraft();
            _marks.Add(uv);
            var dot = UITheme.CreatePanel(rt, "标点 " + _marks.Count, local, new Vector2(17f, 17f),
                UITheme.Accent, UITheme.RadiusSmall);
            var dotGraphic = dot.GetComponent<Graphic>();
            if (dotGraphic != null) dotGraphic.raycastTarget = false;
            var number = UIKit.Label(dot, _marks.Count.ToString(), 10, UITheme.TextOnAccent, TextAnchor.MiddleCenter);
            number.raycastTarget = false;
            _dots.Add(dot.gameObject);
            UpdateStatus();
        }

        private void UndoMark()
        {
            if (_marks.Count == 0) return;
            ClearDraft();
            int last = _marks.Count - 1;
            _marks.RemoveAt(last);
            Destroy(_dots[last]);
            _dots.RemoveAt(last);
            UpdateStatus();
        }

        private void RefitIfReady()
        {
            if (_draft != null) Fit();
        }

        private void Fit()
        {
            ClearDraft();
            if (!PhotoAvatarFitter.TryFit(_source, _marks.ToArray(), _shortHair,
                out var atlas, out var faceScale, out var error))
            {
                UpdateStatus(error);
                return;
            }

            var result = new PhotoAvatarData
            {
                Id = "photo." + Guid.NewGuid().ToString("N"),
                DisplayName = string.IsNullOrWhiteSpace(_name.text) ? "照片演奏者" : _name.text.Trim(),
                ShortHair = _shortHair,
                Outfit = _outfit,
                Complexion = _complexion,
                FaceScale = faceScale,
                FittedAtlas = Convert.ToBase64String(atlas.EncodeToPNG())
            };
            Destroy(atlas);
            if (!Preview(result, out error))
            {
                ClearDraft();
                UpdateStatus(error);
                return;
            }
            _draft = result;
            UpdateStatus();
        }

        private bool Preview(PhotoAvatarData result, out string error)
        {
            _studio = new GameObject("照片角色预览");
            _studio.transform.SetParent(transform, false);
            _studio.transform.position = new Vector3(0f, 100f, -100f);
            _previewProfile = result.CreateProfile();
            _view = RocketboxCharacterView.TryCreate(_previewProfile, _studio.transform, out error);
            if (_view == null || _view.Skeleton?.Head == null)
            {
                error = error ?? "角色头部骨架缺失。";
                return false;
            }
            foreach (var item in _studio.GetComponentsInChildren<Transform>(true))
                item.gameObject.layer = PreviewLayer;
            var cameraRoot = new GameObject("预览相机");
            cameraRoot.transform.SetParent(_studio.transform, false);
            _camera = cameraRoot.AddComponent<Camera>();
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.12f, .16f, .22f);
            _camera.fieldOfView = 34f;
            _camera.nearClipPlane = .04f;
            _camera.farClipPlane = 5f;
            _target = new RenderTexture(512, 344, 24) { name = "照片头像预览" };
            _camera.targetTexture = _target;
            _render.texture = _target;
            _render.enabled = true;
            AddStudioLight(new Vector3(44f, 24f, 0f), new Color(.95f, .91f, .84f), 1.4f);
            AddStudioLight(new Vector3(16f, 146f, 0f), new Color(.65f, .80f, 1f), .8f);
            FramePreview();
            return true;
        }

        private void AddStudioLight(Vector3 rotation, Color color, float intensity)
        {
            var source = new GameObject("预览摄影灯");
            source.transform.SetParent(_studio.transform, false);
            source.transform.localEulerAngles = rotation;
            var light = source.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.color = color;
            light.intensity = intensity;
        }

        private void FlipPreview()
        {
            _side = !_side;
            FramePreview();
        }

        private void FramePreview()
        {
            if (_camera == null || _view?.Skeleton?.Head == null) return;
            Vector3 center = _view.Skeleton.Head.position + Vector3.up * .062f;
            var offset = Quaternion.Euler(0f, _side ? 58f : 0f, 0f) * (_view.RootTransform.forward * .57f);
            _camera.transform.position = center + offset + Vector3.up * .014f;
            _camera.transform.LookAt(center);
        }

        private void Save()
        {
            if (_draft == null) return;
            _draft.DisplayName = string.IsNullOrWhiteSpace(_name.text) ? "照片演奏者" : _name.text.Trim();
            string error = _onSave?.Invoke(_draft);
            if (string.IsNullOrEmpty(error)) Close();
            else UpdateStatus(error);
        }

        private void ReleaseSource()
        {
            if (_photo != null) _photo.texture = null;
            if (_source != null) Destroy(_source);
            _source = null;
            foreach (var dot in _dots) if (dot != null) Destroy(dot);
            _dots.Clear();
            _marks.Clear();
        }

        private void ClearDraft()
        {
            _draft = null;
            if (_camera != null) _camera.enabled = false;
            if (_render != null)
            {
                _render.enabled = false;
                _render.texture = null;
            }
            if (_target != null) { _target.Release(); Destroy(_target); }
            _target = null;
            _view?.DisposeCharacterResources();
            _view = null;
            _camera = null;
            if (_previewProfile != null) Destroy(_previewProfile);
            _previewProfile = null;
            if (_studio != null)
            {
                _studio.SetActive(false);
                Destroy(_studio);
            }
            _studio = null;
        }

        private void UpdateStatus(string message = null)
        {
            _fitButton.interactable = _marks.Count == PhotoAvatarFitter.LandmarkCount;
            _saveButton.interactable = _draft != null;
            _status.color = message == null ? UITheme.TextSecondary : UITheme.TextError;
            _status.text = message ?? (_source == null ? "选择清晰的正脸 JPG 或 PNG，脸部尽量占满画面。" :
                _marks.Count < PhotoAvatarFitter.LandmarkCount
                    ? $"第 {_marks.Count + 1} 步 · 请点击{PhotoAvatarFitter.LandmarkName(_marks.Count)}"
                    : _draft == null ? "标点完成，生成预览后可查看正面和侧面。" : "预览完成。确认角色名后可以保存并上台。");
        }

        private Button PlaceButton(string text, float x, float y, float width, UnityEngine.Events.UnityAction click)
        {
            var button = UITheme.CreateButton(_panel, text, Vector2.zero, new Vector2(width, 34f), click,
                UITheme.FontBodySmall, ButtonStyle.Secondary);
            UITheme.PlaceTopLeft(button.GetComponent<RectTransform>(), x, y, width, 34f);
            return button;
        }

        private Text Label(string text, float x, float y, float width, float height, int fontSize = UITheme.FontSmall)
        {
            var label = UITheme.CreateText(_panel, text, Vector2.zero, new Vector2(width, height), fontSize,
                UITheme.TextPrimary, TextAnchor.MiddleLeft);
            UITheme.PlaceTopLeft(label.rectTransform, x, y, width, height);
            return label;
        }

        private static RawImage MakeImage(Transform parent, string name)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            image.transform.SetParent(parent, false);
            return image.GetComponent<RawImage>();
        }
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GlassShooter.Gameplay
{
    /// <summary>成長項目の縦スクロール一覧とRunボタンを構築します。</summary>
    [DisallowMultipleComponent]
    public sealed class GrowthSceneController : MonoBehaviour
    {
        private const string GameplaySceneName = "New Scene";

        private sealed class RowView
        {
            public TMP_Text value;
            public TMP_Text buttonLabel;
            public Button button;
        }

        [SerializeField] private TMP_FontAsset font;

        private readonly Dictionary<GrowthStatId, RowView> rows = new();
        private ResourceComponent resource;
        private GrowthStatusComponent growth;
        private TMP_Text resourceText;

        private void Start()
        {
            resource = ResourceComponent.Instance;
            growth = GrowthStatusComponent.Instance;
            resource.ResourceChanged += OnResourceChanged;
            growth.Changed += RefreshAll;

            EnsureEventSystem();
            BuildInterface();
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (resource != null)
            {
                resource.ResourceChanged -= OnResourceChanged;
            }
            if (growth != null)
            {
                growth.Changed -= RefreshAll;
            }
        }

        private void BuildInterface()
        {
            if (font == null)
            {
                font = TMP_Settings.defaultFontAsset;
                Debug.LogWarning("成長画面の日本語フォントが未設定です。TextMeshProの既定フォントを使用します。", this);
            }

            GameObject canvasObject = CreateObject("GrowthCanvas", transform);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            Image background = CreateImage("Background", canvasObject.transform, new Color(0.035f, 0.045f, 0.07f, 1f));
            Stretch(background.rectTransform);

            RectTransform header = CreateImage("Header", canvasObject.transform, new Color(0.07f, 0.09f, 0.14f, 1f)).rectTransform;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(0f, -140f);
            header.offsetMax = Vector2.zero;

            TMP_Text title = CreateText("Title", header, "成長", 38, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(34f, 0f), new Vector2(-10f, 0f));

            resourceText = CreateText("Resource", header, string.Empty, 25, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
            SetRect(resourceText.rectTransform, new Vector2(0.48f, 0f), new Vector2(0.76f, 1f), Vector2.zero, new Vector2(-18f, 0f));

            Button runButton = CreateButton("RunButton", header, "ゲーム開始", new Color(0.15f, 0.62f, 0.42f, 1f));
            SetRect(runButton.GetComponent<RectTransform>(), new Vector2(0.77f, 0.18f), new Vector2(0.97f, 0.82f), Vector2.zero, Vector2.zero);
            runButton.onClick.AddListener(RunGame);

            RectTransform scrollArea = CreateObject("ScrollView", canvasObject.transform).GetComponent<RectTransform>();
            SetRect(scrollArea, Vector2.zero, Vector2.one, new Vector2(24f, 28f), new Vector2(-24f, -160f));
            Image scrollBackground = scrollArea.gameObject.AddComponent<Image>();
            scrollBackground.color = new Color(0.045f, 0.06f, 0.095f, 1f);
            ScrollRect scroll = scrollArea.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 36f;

            RectTransform viewport = CreateObject("Viewport", scrollArea).GetComponent<RectTransform>();
            Stretch(viewport);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = Color.white;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = viewport;

            RectTransform content = CreateObject("Content", viewport).GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(18f, 0f);
            content.offsetMax = new Vector2(-18f, 0f);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 16, 24);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            string currentGroup = null;
            foreach (GrowthStatDefinition definition in GrowthStatusComponent.DisplayDefinitions)
            {
                if (definition.Group != currentGroup)
                {
                    currentGroup = definition.Group;
                    CreateGroupHeader(content, currentGroup);
                }
                CreateStatRow(content, definition);
            }
        }

        private void CreateGroupHeader(Transform parent, string label)
        {
            TMP_Text header = CreateText(label + "Header", parent, label, 25, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            header.color = new Color(0.4f, 0.82f, 1f, 1f);
            LayoutElement element = header.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 52f;
        }

        private void CreateStatRow(Transform parent, GrowthStatDefinition definition)
        {
            Image row = CreateImage(definition.Id + "Row", parent, new Color(0.085f, 0.105f, 0.15f, 1f));
            LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 78f;

            TMP_Text label = CreateText("Label", row.transform, definition.Label, 21, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            SetRect(label.rectTransform, Vector2.zero, new Vector2(0.48f, 1f), new Vector2(22f, 0f), Vector2.zero);

            TMP_Text value = CreateText("Value", row.transform, string.Empty, 21, FontStyles.Bold, TextAlignmentOptions.Center);
            SetRect(value.rectTransform, new Vector2(0.47f, 0f), new Vector2(0.72f, 1f), Vector2.zero, Vector2.zero);

            Button button = CreateButton("Upgrade", row.transform, string.Empty, new Color(0.16f, 0.4f, 0.72f, 1f));
            SetRect(button.GetComponent<RectTransform>(), new Vector2(0.74f, 0.14f), new Vector2(0.98f, 0.86f), Vector2.zero, Vector2.zero);
            TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>();
            GrowthStatId capturedId = definition.Id;
            button.onClick.AddListener(() => growth.TryUpgrade(capturedId));

            rows.Add(definition.Id, new RowView
            {
                value = value,
                button = button,
                buttonLabel = buttonLabel
            });
        }

        private void RefreshAll()
        {
            if (resourceText == null || growth == null)
            {
                return;
            }

            resourceText.text = $"資源  {resource.Resource:0.###}";
            foreach (GrowthStatDefinition definition in GrowthStatusComponent.DisplayDefinitions)
            {
                RowView row = rows[definition.Id];
                int level = growth.GetLevel(definition.Id);
                row.value.text = $"{growth.FormatValue(definition.Id)}   レベル {level}";
                bool canUpgrade = growth.CanUpgrade(definition.Id);
                row.button.interactable = canUpgrade && resource.Resource >= growth.GetUpgradeCost(definition.Id);
                row.buttonLabel.text = canUpgrade
                    ? $"強化  {growth.GetUpgradeCost(definition.Id)}"
                    : definition.BaseCost == 0 ? "準備中" : "最大";
            }
        }

        private void OnResourceChanged(float currentResource)
        {
            RefreshAll();
        }

        private static void RunGame()
        {
            if (!Application.CanStreamedLevelBeLoaded(GameplaySceneName))
            {
                Debug.LogError($"Build Settingsに '{GameplaySceneName}' が登録されていません。");
                return;
            }
            SceneManager.LoadScene(GameplaySceneName);
        }

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        private static GameObject CreateObject(string objectName, Transform parent)
        {
            GameObject result = new(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private Image CreateImage(string objectName, Transform parent, Color color)
        {
            GameObject result = CreateObject(objectName, parent);
            Image image = result.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private TMP_Text CreateText(
            string objectName,
            Transform parent,
            string content,
            int size,
            FontStyles style,
            TextAlignmentOptions alignment)
        {
            GameObject result = CreateObject(objectName, parent);
            TextMeshProUGUI text = result.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = new Color(0.92f, 0.95f, 1f, 1f);
            text.richText = false;
            return text;
        }

        private Button CreateButton(string objectName, Transform parent, string label, Color color)
        {
            Image image = CreateImage(objectName, parent, color);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = color * 1.18f;
            colors.pressedColor = color * 0.8f;
            colors.disabledColor = new Color(0.16f, 0.17f, 0.2f, 1f);
            button.colors = colors;
            TMP_Text text = CreateText("Text", button.transform, label, 22, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            SetRect(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}

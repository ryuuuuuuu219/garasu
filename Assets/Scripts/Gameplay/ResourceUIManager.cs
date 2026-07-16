using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GlassShooter.Gameplay
{
    /// <summary>プレイヤーの現在資源をゲーム画面へ表示します。</summary>
    [DisallowMultipleComponent]
    public sealed class ResourceUIManager : MonoBehaviour
    {
        [SerializeField] private TMP_Text resourceText;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string label = "資源";

        private ResourceComponent resource;

        private void Awake()
        {
            EnsureInterface();
        }

        private void OnEnable()
        {
            resource = ResourceComponent.Instance;
            resource.ResourceChanged += Refresh;
            Refresh(resource.Resource);
        }

        private void OnDisable()
        {
            if (resource != null)
            {
                resource.ResourceChanged -= Refresh;
            }
        }

        private void EnsureInterface()
        {
            if (resourceText != null)
            {
                return;
            }

            GameObject canvasObject = new("ResourceCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panelObject = new("ResourcePanel", typeof(RectTransform));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-24f, -24f);
            panelRect.sizeDelta = new Vector2(360f, 72f);
            Image panel = panelObject.AddComponent<Image>();
            panel.color = new Color(0.035f, 0.045f, 0.07f, 0.88f);
            panel.raycastTarget = false;

            GameObject textObject = new("ResourceText", typeof(RectTransform));
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 0f);
            textRect.offsetMax = new Vector2(-20f, 0f);

            TextMeshProUGUI generatedText = textObject.AddComponent<TextMeshProUGUI>();
            generatedText.font = font != null ? font : TMP_Settings.defaultFontAsset;
            generatedText.fontSize = 32f;
            generatedText.fontStyle = FontStyles.Bold;
            generatedText.alignment = TextAlignmentOptions.MidlineRight;
            generatedText.color = new Color(0.92f, 0.95f, 1f, 1f);
            generatedText.raycastTarget = false;
            generatedText.richText = false;
            resourceText = generatedText;
        }

        private void Refresh(float currentResource)
        {
            if (resourceText != null)
            {
                resourceText.text = $"{label}  {currentResource:0.###}";
            }
        }
    }
}

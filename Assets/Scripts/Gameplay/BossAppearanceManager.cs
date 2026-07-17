using GlassShooter.Gameplay;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay
{
    [AddComponentMenu("Scripts/Gameplay/Boss Appearance Manager")]
    public sealed class BossAppearanceManager : MonoBehaviour
    {
        [Header("表示内容")]
        [SerializeField, TextArea(8, 14)]
        private string warningMessage =
            "//警告//\n" +
            "ボス出現\n\n" +
            "モデルケース*A\n" +
            "回復コア\n\n" +
            "機能解析……\n" +
            "・不可侵領域形成\n" +
            "・回復能力\n\n" +
            "カウントダウン開始……";

        [SerializeField] private TMP_FontAsset japaneseFont;

        [Header("時間")]
        [SerializeField, Min(0f)] private float warningDuration = 2.5f;
        [SerializeField, Min(0.01f)] private float panelTransitionDuration = 0.8f;
        [SerializeField, Min(0.01f)] private float convergenceDuration = 1.5f;

        [Header("文字送り・カウントダウン演出")]
        [SerializeField, Min(1f)] private float typewriterCharactersPerSecond = 28f;
        [SerializeField, Min(0.01f)] private float countdownTickPulseDuration = 0.22f;
        [SerializeField, Min(0f)] private float urgencyThresholdSeconds = 5f;

        [Header("警告パネルサイズ")]
        [SerializeField, Min(1f)] private float warningPanelWidth = 650f;
        [SerializeField, Min(1f)] private float warningLineHeight = 42f;
        [SerializeField, Min(0f)] private float warningVerticalPadding = 60f;
        [SerializeField, Min(1f)] private float minimumWarningPanelHeight = 180f;
        [SerializeField, Min(1f)] private float maximumWarningPanelHeight = 900f;

        [Header("収束リング")]
        [SerializeField, Min(1)] private int ringCount = 4;
        [SerializeField, Min(0.1f)] private float ringStartRadius = 6f;
        [SerializeField, Min(0f)] private float ringSpacing = 1.2f;
        [SerializeField, Min(0.001f)] private float ringWidth = 0.05f;
        [SerializeField, Min(12)] private int ringSegments = 64;

        private static readonly Color WarningRed = new Color(0.95f, 0.05f, 0.05f, 1f);
        private const float CountdownSeconds = 30f;

        private readonly List<GameObject> activeRings = new List<GameObject>();
        private Coroutine activePresentation;
        private GameObject[] activeTargets;
        private Material ringMaterial;

        private GameObject uiRoot;
        private RectTransform panelRect;
        private TMP_Text warningText;
        private TMP_Text countdownText;
        private Image gaugeBackground;
        private Image gaugeFill;

        public string BossDescription(GameObject main)
        {
            BossGlassComponent status;
            main.TryGetComponent<BossGlassComponent>(out status);
            string bossname = "";
            if (main.name.StartsWith("Boss_A_core"))
            {
                bossname = "\n回復コア\n";
            }

            string Message =
            "//警告//\n" +
            "ボス出現\n\n" +
            main.name +
            bossname +
            "\n機能解析……\n" +
            "・不可侵領域形成\n" +
            "・回復能力\n\n" +
            "カウントダウン開始……";

            return Message;
        }

        public void apperdelay(params GameObject[] targets)
        {
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            CancelCurrentPresentation();
            activeTargets = targets;
            SetTargetsActive(activeTargets, false);
            activePresentation = StartCoroutine(PlayPresentation(targets));
        }

        private IEnumerator PlayPresentation(GameObject[] targets)
        {
            EnsureUI();
            string description = targets[0] != null ? BossDescription(targets[0]) : warningMessage;
            ShowWarningPanel(description);

            yield return RevealWarningText();
            yield return MovePanelToTop();

            Vector3 appearancePosition = FindAppearancePosition(targets);
            yield return ConvergeRings(appearancePosition);

            SetTargetsActive(targets, true);
            activeTargets = null;

            yield return RunCountdown();

            uiRoot.SetActive(false);
            activePresentation = null;
        }

        private void EnsureUI()
        {
            if (uiRoot != null)
            {
                return;
            }

            uiRoot = new GameObject(
                "BossAppearanceCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            uiRoot.transform.SetParent(transform, false);

            Canvas canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = CreateUIObject("WarningPanel", uiRoot.transform);
            panelRect = panel.GetComponent<RectTransform>();
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = Color.black;
            panel.AddComponent<RectMask2D>();
            CreateRedBorder(panelRect, 5f);

            warningText = CreateText("WarningText", panelRect, 34f, TextAlignmentOptions.Center);
            SetOffsets(warningText.rectTransform, 30f, 30f, 30f, 30f);

            countdownText = CreateText("CountdownText", panelRect, 31f, TextAlignmentOptions.Center);
            RectTransform countdownRect = countdownText.rectTransform;
            countdownRect.anchorMin = new Vector2(0f, 0.42f);
            countdownRect.anchorMax = Vector2.one;
            countdownRect.offsetMin = new Vector2(25f, 0f);
            countdownRect.offsetMax = new Vector2(-25f, -8f);

            gaugeBackground = CreateImage("GaugeBackground", panelRect, new Color(0.2f, 0f, 0f, 1f));
            RectTransform gaugeRect = gaugeBackground.rectTransform;
            gaugeRect.anchorMin = new Vector2(0.04f, 0.18f);
            gaugeRect.anchorMax = new Vector2(0.96f, 0.32f);
            gaugeRect.offsetMin = Vector2.zero;
            gaugeRect.offsetMax = Vector2.zero;
            gaugeFill = CreateImage("GaugeFill", gaugeRect, WarningRed);
            RectTransform fillRect = gaugeFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            gaugeFill.type = Image.Type.Simple;
            SetGaugeRatio(0f);
            CreateRedBorder(gaugeRect, 2f);
        }

        private void ShowWarningPanel(string message)
        {
            uiRoot.SetActive(true);
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(warningPanelWidth, CalculateWarningPanelHeight(message));

            warningText.text = message;
            warningText.maxVisibleCharacters = 0;
            warningText.gameObject.SetActive(true);
            countdownText.gameObject.SetActive(false);
            gaugeBackground.gameObject.SetActive(false);
        }

        private float CalculateWarningPanelHeight(string message)
        {
            int lineCount = 1;
            if (!string.IsNullOrEmpty(message))
            {
                for (int i = 0; i < message.Length; i++)
                {
                    if (message[i] == '\n')
                    {
                        lineCount++;
                    }
                }
            }

            float calculatedHeight = lineCount * warningLineHeight + warningVerticalPadding;
            float minimum = Mathf.Min(minimumWarningPanelHeight, maximumWarningPanelHeight);
            float maximum = Mathf.Max(minimumWarningPanelHeight, maximumWarningPanelHeight);
            return Mathf.Clamp(calculatedHeight, minimum, maximum);
        }

        private IEnumerator MovePanelToTop()
        {
            Vector2 startPosition = panelRect.anchoredPosition;
            Vector2 startSize = panelRect.sizeDelta;
            Vector2 endPosition = new Vector2(0f, 455f);
            Vector2 endSize = new Vector2(1650f, 130f);

            float elapsed = 0f;
            while (elapsed < panelTransitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / panelTransitionDuration));
                panelRect.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, t);
                panelRect.sizeDelta = Vector2.LerpUnclamped(startSize, endSize, t);
                warningText.alpha = 1f - t;
                yield return null;
            }

            panelRect.anchoredPosition = endPosition;
            panelRect.sizeDelta = endSize;
            warningText.alpha = 1f;
            warningText.maxVisibleCharacters = int.MaxValue;
            warningText.gameObject.SetActive(false);
            countdownText.gameObject.SetActive(true);
            gaugeBackground.gameObject.SetActive(true);
            countdownText.text = BuildCountdownLabel(CountdownSeconds);
            countdownText.rectTransform.localScale = Vector3.one;
            countdownText.color = WarningRed;
            SetGaugeRatio(0f);
            gaugeFill.color = WarningRed;
        }

        private IEnumerator RevealWarningText()
        {
            warningText.ForceMeshUpdate();
            int characterCount = warningText.textInfo.characterCount;
            float elapsed = 0f;

            while (warningText.maxVisibleCharacters < characterCount)
            {
                elapsed += Time.unscaledDeltaTime;
                warningText.maxVisibleCharacters = Mathf.Min(
                    characterCount,
                    Mathf.FloorToInt(elapsed * typewriterCharactersPerSecond));
                yield return null;
            }

            while (elapsed < warningDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator ConvergeRings(Vector3 center)
        {
            CreateRings();
            float elapsed = 0f;

            while (elapsed < convergenceDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / convergenceDuration);
                float eased = normalized * normalized;

                for (int i = 0; i < activeRings.Count; i++)
                {
                    float startRadius = ringStartRadius + ringSpacing * i;
                    float endRadius = 0.15f + 0.08f * i;
                    float radius = Mathf.Lerp(startRadius, endRadius, eased);
                    UpdateRing(activeRings[i].GetComponent<LineRenderer>(), center, radius);
                }

                yield return null;
            }

            CleanupRings();
        }

        private void CreateRings()
        {
            Shader shader = Shader.Find("Sprites/Default");
            ringMaterial = shader != null ? new Material(shader) : null;

            for (int i = 0; i < ringCount; i++)
            {
                GameObject ringObject = new GameObject($"BossConvergenceRing_{i}");
                ringObject.transform.SetParent(transform, false);
                LineRenderer line = ringObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.positionCount = Mathf.Max(12, ringSegments);
                line.startWidth = ringWidth;
                line.endWidth = ringWidth;
                line.startColor = Color.white;
                line.endColor = Color.white;
                line.numCornerVertices = 2;
                line.sortingOrder = 500;
                if (ringMaterial != null)
                {
                    line.sharedMaterial = ringMaterial;
                }
                activeRings.Add(ringObject);
            }
        }

        private void UpdateRing(LineRenderer line, Vector3 center, float radius)
        {
            int segments = line.positionCount;
            center.z = -0.1f;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                line.SetPosition(
                    i,
                    center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        private IEnumerator RunCountdown()
        {
            float remaining = CountdownSeconds;
            float pulseRemaining = 0f;
            int previousDisplayedSecond = -1;
            SetGaugeRatio(1f);

            while (remaining > 0f)
            {
                float deltaTime = Time.unscaledDeltaTime;
                int displayedSecond = Mathf.CeilToInt(remaining);
                if (displayedSecond != previousDisplayedSecond)
                {
                    previousDisplayedSecond = displayedSecond;
                    pulseRemaining = countdownTickPulseDuration;
                }

                countdownText.text = BuildCountdownLabel(remaining);

                pulseRemaining = Mathf.Max(0f, pulseRemaining - deltaTime);
                float pulse = pulseRemaining / countdownTickPulseDuration;
                countdownText.rectTransform.localScale = Vector3.one * (1f + 0.18f * pulse);

                float urgencyPulse = remaining <= urgencyThresholdSeconds
                    ? (Mathf.Sin(Time.unscaledTime * 10f) + 1f) * 0.5f
                    : 0f;
                Color animatedColor = Color.Lerp(WarningRed, Color.white, urgencyPulse * 0.65f);
                countdownText.color = animatedColor;
                gaugeFill.color = animatedColor;

                SetGaugeRatio(remaining / CountdownSeconds);

                remaining -= deltaTime;
                yield return null;
            }

            countdownText.text = BuildCountdownLabel(0f);
            countdownText.rectTransform.localScale = Vector3.one;
            countdownText.color = WarningRed;
            SetGaugeRatio(0f);
            gaugeFill.color = WarningRed;
        }

        private void SetGaugeRatio(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);
            RectTransform fillRect = gaugeFill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(ratio, 1f);
            fillRect.offsetMin = new Vector2(3f, 3f);
            fillRect.offsetMax = new Vector2(3f - 6f * ratio, -3f);
            gaugeFill.enabled = ratio > 0f;
        }

        private string BuildCountdownLabel(float remaining)
        {
            return $"カウントダウン……{Mathf.CeilToInt(remaining):00}s";
        }

        private TMP_Text CreateText(
            string objectName,
            Transform parent,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = CreateUIObject(objectName, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.color = WarningRed;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Masking;
            text.raycastTarget = false;
            if (japaneseFont != null)
            {
                text.font = japaneseFont;
            }
            return text;
        }

        private static Image CreateImage(string objectName, Transform parent, Color color)
        {
            GameObject imageObject = CreateUIObject(objectName, parent);
            Image image = imageObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static GameObject CreateUIObject(string objectName, Transform parent)
        {
            GameObject result = new GameObject(objectName, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void CreateRedBorder(RectTransform parent, float thickness)
        {
            CreateBorderEdge("Top", parent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero);
            CreateBorderEdge("Bottom", parent, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness));
            CreateBorderEdge("Left", parent, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f));
            CreateBorderEdge("Right", parent, new Vector2(1f, 0f), Vector2.one, new Vector2(-thickness, 0f), Vector2.zero);
        }

        private static void CreateBorderEdge(
            string edgeName,
            RectTransform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            Image edge = CreateImage(edgeName, parent, WarningRed);
            RectTransform rect = edge.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetOffsets(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static Vector3 FindAppearancePosition(GameObject[] targets)
        {
            foreach (GameObject target in targets)
            {
                if (target != null)
                {
                    return target.transform.position;
                }
            }
            return Vector3.zero;
        }

        private static void SetTargetsActive(GameObject[] targets, bool isActive)
        {
            if (targets == null)
            {
                return;
            }

            foreach (GameObject target in targets)
            {
                if (target != null)
                {
                    target.SetActive(isActive);
                }
            }
        }

        private void CancelCurrentPresentation()
        {
            if (activePresentation != null)
            {
                StopCoroutine(activePresentation);
                activePresentation = null;
            }

            SetTargetsActive(activeTargets, true);
            activeTargets = null;
            CleanupRings();
            if (uiRoot != null)
            {
                uiRoot.SetActive(false);
            }
        }

        private void CleanupRings()
        {
            foreach (GameObject ring in activeRings)
            {
                if (ring != null)
                {
                    Destroy(ring);
                }
            }
            activeRings.Clear();

            if (ringMaterial != null)
            {
                Destroy(ringMaterial);
                ringMaterial = null;
            }
        }

        private void OnDisable()
        {
            CancelCurrentPresentation();
        }
    }
}

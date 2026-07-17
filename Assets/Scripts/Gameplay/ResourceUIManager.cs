using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace GlassShooter.Gameplay
{
    /// <summary>現在資源と、クラック・破片から得られるスコアを表示します。</summary>
    [DisallowMultipleComponent]
    public sealed class ResourceUIManager : MonoBehaviour
    {
        [Header("Score Popups")]
        [FormerlySerializedAs("canvas1")]
        [SerializeField] private Canvas scoreCanvas;
        [FormerlySerializedAs("UIprefab")]
        [SerializeField] private TMP_Text crackScorePrefab;
        [FormerlySerializedAs("UIprefab2")]
        [SerializeField] private TMP_Text fragmentScorePrefab;
        [SerializeField, Min(0.01f)] private float crackScoreLifetime = 1.25f;
        [SerializeField, Min(0f)] private float crackRiseSpeed = 45f;
        [SerializeField, Min(0f)] private float fragmentOffset = 32f;

        [Header("Current Resource")]
        [SerializeField] private TMP_Text resourceText;
        [SerializeField] private string label = "資源";

        private ResourceComponent resource;
        private bool missingPopupWarningLogged;

        public static ResourceUIManager Instance { get; private set; }

        private void OnEnable()
        {
            Instance = this;
            resource = ResourceComponent.Instance;
            resource.ResourceChanged += Refresh;
            Refresh(resource.Resource);
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (resource != null)
            {
                resource.ResourceChanged -= Refresh;
            }
        }

        private void Refresh(float currentResource)
        {
            if (resourceText != null)
            {
                resourceText.text = $"{label}  {currentResource:0.###}";
            }
        }

        /// <summary>新しく生成されたクラック線分の位置から獲得スコアを浮かせます。</summary>
        public void ShowCrackScore(Vector3 worldPosition, float score)
        {
            if (score <= 0f)
            {
                return;
            }

            FloatingResourceText popup = CreatePopup(crackScorePrefab, score);
            if (popup == null)
            {
                return;
            }

            popup.InitializeFloating(
                ResolveCanvas(),
                worldPosition,
                crackRiseSpeed,
                crackScoreLifetime);
        }

        /// <summary>破片が存在する間、回収予定スコアを破片の直上へ表示します。</summary>
        public void ShowFragmentScore(GameObject fragment, float score)
        {
            if (fragment == null || score <= 0f)
            {
                return;
            }

            FloatingResourceText popup = CreatePopup(fragmentScorePrefab, score);
            if (popup == null)
            {
                return;
            }

            popup.InitializeFollowing(
                ResolveCanvas(),
                fragment,
                new Vector2(0f, fragmentOffset));
        }

        private FloatingResourceText CreatePopup(TMP_Text preferredPrefab, float score)
        {
            Canvas canvas = ResolveCanvas();
            TMP_Text template = preferredPrefab != null ? preferredPrefab : resourceText;
            if (canvas == null || template == null)
            {
                if (!missingPopupWarningLogged)
                {
                    Debug.LogWarning(
                        "Score popup requires a Canvas and a TMP_Text prefab or resource text fallback.",
                        this);
                    missingPopupWarningLogged = true;
                }
                return null;
            }

            TMP_Text text = Instantiate(template, canvas.transform, false);
            text.name = "ResourceScorePopup";
            text.text = $"+{score:0.###}";
            text.raycastTarget = false;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            FloatingResourceText popup = text.GetComponent<FloatingResourceText>();
            return popup != null ? popup : text.gameObject.AddComponent<FloatingResourceText>();
        }

        private Canvas ResolveCanvas()
        {
            if (scoreCanvas == null && resourceText != null)
            {
                scoreCanvas = resourceText.canvas;
            }
            return scoreCanvas;
        }
    }

    internal sealed class FloatingResourceText : MonoBehaviour
    {
        private TMP_Text text;
        private Canvas canvas;
        private Camera worldCamera;
        private GameObject followedObject;
        private Vector3 worldPosition;
        private Vector2 screenOffset;
        private float riseSpeed;
        private float lifetime;
        private float elapsedTime;
        private Color initialColor;
        private bool followsObject;

        public void InitializeFloating(
            Canvas targetCanvas,
            Vector3 position,
            float pixelsPerSecond,
            float duration)
        {
            Initialize(targetCanvas);
            worldPosition = position;
            riseSpeed = pixelsPerSecond;
            lifetime = Mathf.Max(0.01f, duration);
            followsObject = false;
            UpdatePosition();
        }

        public void InitializeFollowing(
            Canvas targetCanvas,
            GameObject target,
            Vector2 offset)
        {
            Initialize(targetCanvas);
            followedObject = target;
            screenOffset = offset;
            followsObject = true;
            UpdatePosition();
        }

        private void Initialize(Canvas targetCanvas)
        {
            text = GetComponent<TMP_Text>();
            canvas = targetCanvas;
            worldCamera = Camera.main;
            initialColor = text != null ? text.color : Color.white;
        }

        private void LateUpdate()
        {
            if (followsObject && followedObject == null)
            {
                Destroy(gameObject);
                return;
            }

            elapsedTime += Time.deltaTime;
            if (!followsObject)
            {
                screenOffset.y += riseSpeed * Time.deltaTime;
                if (text != null)
                {
                    Color color = initialColor;
                    color.a *= 1f - Mathf.Clamp01(elapsedTime / lifetime);
                    text.color = color;
                }
                if (elapsedTime >= lifetime)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (canvas == null || worldCamera == null)
            {
                return;
            }

            Vector3 targetWorldPosition = followsObject
                ? followedObject.transform.position
                : worldPosition;
            Vector2 screenPosition = worldCamera.WorldToScreenPoint(targetWorldPosition);
            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                screenPosition,
                canvasCamera,
                out Vector2 localPosition))
            {
                transform.localPosition = localPosition + screenOffset;
            }
        }
    }
}

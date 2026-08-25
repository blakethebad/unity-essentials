using System;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The canvas configuration a window is built with, authored inside <see cref="WindowData"/> and
    /// written onto the freshly created components by <see cref="Apply"/>. Windows always render as
    /// a screen-space overlay canvas. Properties are get-only: values are applied once, at load.
    /// </summary>
    [Serializable]
    public sealed class CanvasSettings
    {
        private const string DefaultSortingLayerName = "Default";
        private const float DefaultReferenceWidth = 1920f;
        private const float DefaultReferenceHeight = 1080f;
        private const float DefaultMatchWidthOrHeight = 0.5f;
        private const float DefaultScaleFactor = 1f;
        private const float DefaultReferencePixelsPerUnit = 100f;
        private const float DefaultScreenDPI = 96f;
        private const float MinimumPositive = 0.0001f;

        [Tooltip("Sorting layer the whole canvas is drawn in, relative to other renderers.")]
        [SerializeField] private string sortingLayerName;

        [Tooltip("Order within the sorting layer. Higher draws later, on top of lower values.")]
        [SerializeField] private int sortingOrder;

        [Tooltip("Draw UI at exact pixel positions. Sharpens static UI; can make animated movement stutter.")]
        [SerializeField] private bool pixelPerfect;

        [Tooltip("Display index this canvas renders to. Leave at 0 for single-display games.")]
        [SerializeField] private int targetDisplay;

        [Tooltip("How the scaler converts reference pixels to screen pixels. Scale With Screen Size is the " +
                 "usual choice for a game that must run on many resolutions.")]
        [SerializeField] private CanvasScaler.ScaleMode uiScaleMode;

        [Tooltip("Constant multiplier applied to every UI element. Constant Pixel Size mode only.")]
        [SerializeField] private float scaleFactor;

        [Tooltip("Resolution the UI was designed against. Scale With Screen Size mode only.")]
        [SerializeField] private Vector2 referenceResolution;

        [Tooltip("How the reference resolution is matched to the real screen. Scale With Screen Size mode only.")]
        [SerializeField] private CanvasScaler.ScreenMatchMode screenMatchMode;

        [Tooltip("0 matches width only, 1 matches height only, 0.5 splits the difference. " +
                 "Match Width Or Height mode only.")]
        [SerializeField] private float matchWidthOrHeight;

        [Tooltip("Pixels per unit the UI assumes for sprites, used when converting sprite sizes to UI sizes.")]
        [SerializeField] private float referencePixelsPerUnit;

        [Tooltip("Physical unit sizes are specified in. Constant Physical Size mode only.")]
        [SerializeField] private CanvasScaler.Unit physicalUnit;

        [Tooltip("DPI assumed when the device does not report one. Constant Physical Size mode only.")]
        [SerializeField] private float fallbackScreenDPI;

        [Tooltip("DPI sprites are assumed to be authored at. Constant Physical Size mode only.")]
        [SerializeField] private float defaultSpriteDPI;

        [Tooltip("Ignore graphics facing away from the camera when raycasting. Leave on unless you " +
                 "deliberately rotate UI past 90 degrees and still want it clickable.")]
        [SerializeField] private bool ignoreReversedGraphics;

        [Tooltip("Which colliders block UI raycasts, letting world geometry occlude the UI.")]
        [SerializeField] private GraphicRaycaster.BlockingObjects blockingObjects;

        [Tooltip("Layers considered when Blocking Objects is set. Ignored when it is None.")]
        [SerializeField] private LayerMask blockingMask;

        /// <summary>
        /// Creates settings at the defaults: scale with screen size against a 1920x1080 reference
        /// resolution with a 0.5 width/height match, and Unity's own defaults for everything else.
        /// </summary>
        public CanvasSettings()
        {
            sortingLayerName = DefaultSortingLayerName;
            sortingOrder = 0;
            pixelPerfect = false;
            targetDisplay = 0;

            uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaleFactor = DefaultScaleFactor;
            referenceResolution = new Vector2(DefaultReferenceWidth, DefaultReferenceHeight);
            screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            matchWidthOrHeight = DefaultMatchWidthOrHeight;
            referencePixelsPerUnit = DefaultReferencePixelsPerUnit;
            physicalUnit = CanvasScaler.Unit.Points;
            fallbackScreenDPI = DefaultScreenDPI;
            defaultSpriteDPI = DefaultScreenDPI;

            ignoreReversedGraphics = true;
            blockingObjects = GraphicRaycaster.BlockingObjects.None;
            blockingMask = ~0;
        }

        public string SortingLayerName => sortingLayerName;

        public int SortingOrder => sortingOrder;

        public bool PixelPerfect => pixelPerfect;

        public int TargetDisplay => targetDisplay;

        public CanvasScaler.ScaleMode UIScaleMode => uiScaleMode;

        public float ScaleFactor => scaleFactor;

        public Vector2 ReferenceResolution => referenceResolution;

        public CanvasScaler.ScreenMatchMode ScreenMatchMode => screenMatchMode;

        public float MatchWidthOrHeight => matchWidthOrHeight;

        public float ReferencePixelsPerUnit => referencePixelsPerUnit;

        public CanvasScaler.Unit PhysicalUnit => physicalUnit;

        public float FallbackScreenDPI => fallbackScreenDPI;

        public float DefaultSpriteDPI => defaultSpriteDPI;

        public bool IgnoreReversedGraphics => ignoreReversedGraphics;

        public GraphicRaycaster.BlockingObjects BlockingObjects => blockingObjects;

        public LayerMask BlockingMask => blockingMask;

        internal void Normalize()
        {
            // Called from WindowData.OnValidate on every inspector keystroke — must never throw.
            // Non-finite values are restored to defaults because Clamp01/Max pass NaN through.
            if (string.IsNullOrEmpty(sortingLayerName))
            {
                sortingLayerName = DefaultSortingLayerName;
            }

            matchWidthOrHeight = IsNonFinite(matchWidthOrHeight)
                ? DefaultMatchWidthOrHeight
                : Mathf.Clamp01(matchWidthOrHeight);

            referenceResolution = new Vector2(
                ClampAtLeastOne(referenceResolution.x, DefaultReferenceWidth),
                ClampAtLeastOne(referenceResolution.y, DefaultReferenceHeight));

            scaleFactor = ClampPositive(scaleFactor, DefaultScaleFactor);
            referencePixelsPerUnit = ClampPositive(referencePixelsPerUnit, DefaultReferencePixelsPerUnit);
            fallbackScreenDPI = ClampPositive(fallbackScreenDPI, DefaultScreenDPI);
            defaultSpriteDPI = ClampPositive(defaultSpriteDPI, DefaultScreenDPI);
        }

        internal void Apply(Canvas c, CanvasScaler s, GraphicRaycaster r)
        {
            if (c == null)
            {
                throw new ArgumentNullException(nameof(c));
            }

            if (s == null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (r == null)
            {
                throw new ArgumentNullException(nameof(r));
            }

            // Every field is written unconditionally so freshly created components never keep
            // Unity's defaults where the asset authored something else.
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.worldCamera = null;
            c.pixelPerfect = pixelPerfect;
            c.sortingLayerName = sortingLayerName;
            c.sortingOrder = sortingOrder;
            c.targetDisplay = targetDisplay;

            s.uiScaleMode = uiScaleMode;
            s.scaleFactor = scaleFactor;
            s.referenceResolution = referenceResolution;
            s.screenMatchMode = screenMatchMode;
            s.matchWidthOrHeight = matchWidthOrHeight;
            s.referencePixelsPerUnit = referencePixelsPerUnit;
            s.physicalUnit = physicalUnit;
            s.fallbackScreenDPI = fallbackScreenDPI;
            s.defaultSpriteDPI = defaultSpriteDPI;

            r.ignoreReversedGraphics = ignoreReversedGraphics;
            r.blockingObjects = blockingObjects;
            r.blockingMask = blockingMask;
        }

        private static float ClampPositive(float value, float fallback)
        {
            return IsNonFinite(value) ? fallback : Mathf.Max(MinimumPositive, value);
        }

        private static float ClampAtLeastOne(float value, float fallback)
        {
            return IsNonFinite(value) ? fallback : Mathf.Max(1f, value);
        }

        // float.IsFinite does not exist in the 4.7.1 API profile Unity compiles against.
        private static bool IsNonFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }
    }
}

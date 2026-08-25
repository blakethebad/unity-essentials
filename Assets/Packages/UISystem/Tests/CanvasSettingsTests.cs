using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// <see cref="CanvasSettings"/> end to end: the defaults, the inspector-time Normalize pass
    /// that repairs numbers and never throws, and the Apply call asserted field by field against
    /// real components — an unwritten field produces UI that is subtly wrong, not an error.
    /// </summary>
    [TestFixture]
    public class CanvasSettingsTests : UITestFixture
    {
        // ---- Defaults ---------------------------------------------------------

        [Test]
        public void Constructor_DefaultsToScaleWithScreenSize()
        {
            var settings = new CanvasSettings();

            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, settings.UIScaleMode);
        }

        [Test]
        public void Constructor_DefaultsToAFullHdReferenceResolution()
        {
            var settings = new CanvasSettings();

            Assert.AreEqual(new Vector2(1920f, 1080f), settings.ReferenceResolution);
        }

        [Test]
        public void Constructor_DefaultsToAnEvenWidthHeightMatch()
        {
            var settings = new CanvasSettings();

            Assert.AreEqual(0.5f, settings.MatchWidthOrHeight, 0.0001f);
        }

        // ---- Normalize -----------------------------------------------------------

        // Unity extrapolates an out-of-range match instead of clamping it, so the clamp lives here.
        [Test]
        public void Normalize_MatchAboveOne_ClampsToOne()
        {
            var settings = BuildCanvasSettings("{\"matchWidthOrHeight\":2.5}");

            settings.Normalize();

            Assert.AreEqual(1f, settings.MatchWidthOrHeight, 0.0001f);
        }

        [Test]
        public void Normalize_MatchBelowZero_ClampsToZero()
        {
            var settings = BuildCanvasSettings("{\"matchWidthOrHeight\":-1.5}");

            settings.Normalize();

            Assert.AreEqual(0f, settings.MatchWidthOrHeight, 0.0001f);
        }

        // NaN survives Mathf.Clamp01 untouched, so it is tested for explicitly.
        [Test]
        public void Normalize_NonFiniteMatch_RestoresTheDefault()
        {
            var settings = OverwriteField(new CanvasSettings(), "matchWidthOrHeight", float.NaN);

            settings.Normalize();

            Assert.AreEqual(0.5f, settings.MatchWidthOrHeight, 0.0001f);
        }

        [Test]
        public void Normalize_ReferenceResolutionBelowOne_RaisesBothComponents()
        {
            var settings = BuildCanvasSettings("{\"referenceResolution\":{\"x\":0.0,\"y\":-40.0}}");

            settings.Normalize();

            Assert.AreEqual(new Vector2(1f, 1f), settings.ReferenceResolution);
        }

        [Test]
        public void Normalize_NonFiniteReferenceResolution_RestoresTheDefault()
        {
            var settings = OverwriteField(
                new CanvasSettings(),
                "referenceResolution",
                new Vector2(float.NaN, float.PositiveInfinity));

            settings.Normalize();

            Assert.AreEqual(new Vector2(1920f, 1080f), settings.ReferenceResolution);
        }

        [Test]
        public void Normalize_ZeroScaleFactor_RaisesItAboveZero()
        {
            var settings = BuildCanvasSettings("{\"scaleFactor\":0.0}");

            settings.Normalize();

            Assert.Greater(settings.ScaleFactor, 0f);
        }

        [Test]
        public void Normalize_ZeroReferencePixelsPerUnit_RaisesItAboveZero()
        {
            var settings = BuildCanvasSettings("{\"referencePixelsPerUnit\":0.0}");

            settings.Normalize();

            Assert.Greater(settings.ReferencePixelsPerUnit, 0f);
        }

        [Test]
        public void Normalize_ZeroFallbackScreenDpi_RaisesItAboveZero()
        {
            var settings = BuildCanvasSettings("{\"fallbackScreenDPI\":0.0}");

            settings.Normalize();

            Assert.Greater(settings.FallbackScreenDPI, 0f);
        }

        [Test]
        public void Normalize_ZeroDefaultSpriteDpi_RaisesItAboveZero()
        {
            var settings = BuildCanvasSettings("{\"defaultSpriteDPI\":0.0}");

            settings.Normalize();

            Assert.Greater(settings.DefaultSpriteDPI, 0f);
        }

        [Test]
        public void Normalize_EmptySortingLayerName_RestoresTheDefaultLayer()
        {
            var settings = BuildCanvasSettings("{\"sortingLayerName\":\"\"}");

            settings.Normalize();

            Assert.AreEqual("Default", settings.SortingLayerName);
        }

        // Clamping repairs only what would divide by zero or extrapolate; unusual-but-legal values
        // must come through untouched.
        [Test]
        public void Normalize_LeavesLegalValuesUntouched()
        {
            var settings = BuildCanvasSettings(
                "{\"matchWidthOrHeight\":0.25,\"referenceResolution\":{\"x\":800.0,\"y\":600.0}}");

            settings.Normalize();

            Assert.AreEqual(0.25f, settings.MatchWidthOrHeight, 0.0001f);
            Assert.AreEqual(new Vector2(800f, 600f), settings.ReferenceResolution);
        }

        [Test]
        public void Normalize_WithEveryFieldNonFinite_DoesNotThrow()
        {
            var settings = BuildNonFiniteSettings();

            Assert.DoesNotThrow(() => settings.Normalize());
        }

        [Test]
        public void Normalize_RunTwice_IsIdempotent()
        {
            var settings = BuildNonFiniteSettings();
            settings.Normalize();
            var afterFirst = settings.MatchWidthOrHeight;
            var resolutionAfterFirst = settings.ReferenceResolution;

            settings.Normalize();

            Assert.AreEqual(afterFirst, settings.MatchWidthOrHeight, 0.0001f);
            Assert.AreEqual(resolutionAfterFirst, settings.ReferenceResolution);
        }

        // ---- Apply -------------------------------------------------------------

        [Test]
        public void Apply_AlwaysConfiguresAScreenSpaceOverlayCanvas()
        {
            var settings = new CanvasSettings();
            var host = BuildCanvasHost();
            var canvas = host.GetComponent<Canvas>();
            canvas.worldCamera = Track(new GameObject("StaleCamera", typeof(Camera))).GetComponent<Camera>();

            settings.Apply(canvas, host.GetComponent<CanvasScaler>(), host.GetComponent<GraphicRaycaster>());

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.IsTrue(canvas.worldCamera == null, "Apply must clear any stale camera reference.");
        }

        [Test]
        public void Apply_WritesEveryCanvasSettingOntoTheCanvas()
        {
            var settings = BuildCanvasSettings(
                "{\"pixelPerfect\":true,\"sortingLayerName\":\"Default\"," +
                "\"sortingOrder\":7,\"targetDisplay\":1}");
            var host = BuildCanvasHost();
            var canvas = host.GetComponent<Canvas>();

            settings.Apply(canvas, host.GetComponent<CanvasScaler>(), host.GetComponent<GraphicRaycaster>());

            Assert.IsTrue(canvas.pixelPerfect);
            Assert.AreEqual("Default", canvas.sortingLayerName);
            Assert.AreEqual(7, canvas.sortingOrder);
            Assert.AreEqual(1, canvas.targetDisplay);
        }

        // Every scaler knob is written unconditionally, including ones the selected mode does not
        // read, so switching mode later never produces values nobody authored.
        [Test]
        public void Apply_WritesEveryScalerSettingOntoTheCanvasScaler()
        {
            var settings = BuildCanvasSettings(
                "{\"uiScaleMode\":2,\"scaleFactor\":2.5,\"referenceResolution\":{\"x\":800.0,\"y\":600.0}," +
                "\"screenMatchMode\":2,\"matchWidthOrHeight\":0.25,\"referencePixelsPerUnit\":50.0," +
                "\"physicalUnit\":2,\"fallbackScreenDPI\":160.0,\"defaultSpriteDPI\":72.0}");
            var host = BuildCanvasHost();
            var scaler = host.GetComponent<CanvasScaler>();

            settings.Apply(host.GetComponent<Canvas>(), scaler, host.GetComponent<GraphicRaycaster>());

            Assert.AreEqual(CanvasScaler.ScaleMode.ConstantPhysicalSize, scaler.uiScaleMode);
            Assert.AreEqual(2.5f, scaler.scaleFactor, 0.0001f);
            Assert.AreEqual(new Vector2(800f, 600f), scaler.referenceResolution);
            Assert.AreEqual(CanvasScaler.ScreenMatchMode.Shrink, scaler.screenMatchMode);
            Assert.AreEqual(0.25f, scaler.matchWidthOrHeight, 0.0001f);
            Assert.AreEqual(50f, scaler.referencePixelsPerUnit, 0.0001f);
            Assert.AreEqual(CanvasScaler.Unit.Inches, scaler.physicalUnit);
            Assert.AreEqual(160f, scaler.fallbackScreenDPI, 0.0001f);
            Assert.AreEqual(72f, scaler.defaultSpriteDPI, 0.0001f);
        }

        [Test]
        public void Apply_WritesEveryRaycasterSettingOntoTheGraphicRaycaster()
        {
            var settings = OverwriteField(
                BuildCanvasSettings("{\"ignoreReversedGraphics\":false,\"blockingObjects\":3}"),
                "blockingMask",
                (LayerMask)5);
            var host = BuildCanvasHost();
            var raycaster = host.GetComponent<GraphicRaycaster>();

            settings.Apply(host.GetComponent<Canvas>(), host.GetComponent<CanvasScaler>(), raycaster);

            Assert.IsFalse(raycaster.ignoreReversedGraphics);
            Assert.AreEqual(GraphicRaycaster.BlockingObjects.All, raycaster.blockingObjects);
            Assert.AreEqual(5, raycaster.blockingMask.value);
        }

        // ---- Helpers ----------------------------------------------------------

        private GameObject BuildCanvasHost()
        {
            return Track(new GameObject(
                "CanvasHost",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)));
        }

        // Assembled by reflection: JsonUtility has no literal for NaN/infinity, and a LayerMask's
        // JSON shape is an internal detail of Unity's serializer.
        private static CanvasSettings BuildNonFiniteSettings()
        {
            var settings = new CanvasSettings();
            OverwriteField(settings, "matchWidthOrHeight", float.NaN);
            OverwriteField(settings, "referenceResolution", new Vector2(float.NaN, float.NegativeInfinity));
            OverwriteField(settings, "scaleFactor", float.PositiveInfinity);
            OverwriteField(settings, "referencePixelsPerUnit", float.NaN);
            OverwriteField(settings, "fallbackScreenDPI", float.NegativeInfinity);
            OverwriteField(settings, "defaultSpriteDPI", float.NaN);
            OverwriteField(settings, "sortingLayerName", null);
            return settings;
        }

        private static CanvasSettings OverwriteField(CanvasSettings settings, string fieldName, object value)
        {
            var field = typeof(CanvasSettings).GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(
                field,
                $"CanvasSettings has no serialized field named '{fieldName}'. Renaming a serialized field " +
                "also breaks every authored asset, so the test is asserting on the same name Unity is.");

            field.SetValue(settings, value);
            return settings;
        }
    }
}

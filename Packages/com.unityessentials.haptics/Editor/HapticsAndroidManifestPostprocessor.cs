// The entire file is compiled only when Android is the active build target. UnityEditor.Android
// ships with the Android Build Support module, which the Editor assembly deliberately does not
// reference: an explicit reference raises a standing "Unable to resolve reference" error on any
// machine without that module. UNITY_ANDROID is defined only when Android is selected, which implies
// the module is present, so this guard trades a compile error on every other machine for a file that
// compiles to nothing there.
#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace UnityEssentials.Haptics.Editor
{
    /// <summary>
    /// Injects the <c>android.permission.VIBRATE</c> permission into the generated Gradle project so
    /// the Android provider can actually vibrate. Runs automatically after every Android build; no
    /// setup, no menu item, nothing for the consumer to remember.
    /// </summary>
    /// <remarks>
    /// The injection is mandatory, not a convenience. Unity's automatic permission detection scans for
    /// <c>Handheld.Vibrate</c> and adds the permission when it finds it; this package never calls that
    /// API — it reaches <c>android.os.Vibrator</c> through JNI, which the scanner cannot see — so
    /// without this callback the merged manifest ships no VIBRATE permission and every vibration is
    /// silently dropped at runtime.
    /// <para>
    /// The permission is injected rather than shipped as an <c>AndroidManifest.xml</c> inside the
    /// package for three reasons. A manifest at the package root is Unity's custom main-manifest
    /// override, which a reusable library must never claim on the consuming project's behalf. A bare
    /// manifest nested elsewhere in <c>Assets</c> is not a documented merge mechanism and is silently
    /// ignored. And an <c>.androidlib</c> folder brings its own Gradle module along with the merge
    /// conflicts that come with it. Writing one element into the generated project keeps the package a
    /// plain folder of source files.
    /// </para>
    /// <para>
    /// The write is idempotent: an existing VIBRATE declaration — whether the consumer added it, or
    /// another postprocessor did — is detected and left alone, so the element never accumulates
    /// duplicates across repeated or appended builds.
    /// </para>
    /// </remarks>
    public sealed class HapticsAndroidManifestPostprocessor : IPostGenerateGradleAndroidProject
    {
        /// <summary>The Android XML namespace every <c>android:</c>-prefixed manifest attribute lives in.</summary>
        private const string AndroidNamespaceUri = "http://schemas.android.com/apk/res/android";

        /// <summary>The conventional prefix bound to <see cref="AndroidNamespaceUri"/> in a manifest.</summary>
        private const string AndroidNamespacePrefix = "android";

        /// <summary>The permission required to use <c>android.os.Vibrator</c> at all.</summary>
        private const string VibratePermission = "android.permission.VIBRATE";

        /// <summary>The manifest element name that declares a required permission.</summary>
        private const string UsesPermissionElement = "uses-permission";

        /// <summary>Path of the module manifest relative to the Gradle module root Unity hands us.</summary>
        private static readonly string ManifestRelativePath = Path.Combine("src", "main", "AndroidManifest.xml");

        /// <summary>
        /// Runs at the default priority. The element is appended to the module manifest before Gradle
        /// merges it, so ordering against other manifest postprocessors does not matter.
        /// </summary>
        public int callbackOrder
        {
            get { return 0; }
        }

        /// <summary>
        /// Adds <c>&lt;uses-permission android:name="android.permission.VIBRATE" /&gt;</c> to the
        /// generated module manifest unless it is already declared there.
        /// </summary>
        /// <param name="path">
        /// The root of the generated <c>unityLibrary</c> Gradle module — not the manifest itself and
        /// not the project root — so the manifest is found at <c>src/main/AndroidManifest.xml</c>
        /// beneath it.
        /// </param>
        /// <remarks>
        /// Problems are logged as errors rather than thrown. A build that fails outright is worse than
        /// one that ships without haptics, but the message has to be loud: the symptom on device is a
        /// device that simply never vibrates, with nothing in the player log to explain it.
        /// </remarks>
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, ManifestRelativePath);
            if (!File.Exists(manifestPath))
            {
                LogInjectionFailed("no manifest was found at \"" + manifestPath + "\"");
                return;
            }

            var document = new XmlDocument();
            document.Load(manifestPath);

            XmlElement manifest = document.DocumentElement;
            if (manifest == null || manifest.Name != "manifest")
            {
                LogInjectionFailed("\"" + manifestPath + "\" has no <manifest> root element");
                return;
            }

            if (HasVibratePermission(manifest))
            {
                return;
            }

            manifest.AppendChild(CreateVibratePermission(document));
            document.Save(manifestPath);
        }

        /// <summary>
        /// Reports whether the manifest already declares the VIBRATE permission, so the injection can
        /// be skipped.
        /// </summary>
        /// <param name="manifest">The manifest's root element.</param>
        /// <returns>True when a <c>uses-permission</c> child already names VIBRATE.</returns>
        /// <remarks>
        /// The attribute is read by namespace rather than by the literal name <c>android:name</c>: the
        /// prefix bound to the Android namespace is chosen by whoever wrote the file, and only the
        /// namespace itself is guaranteed.
        /// </remarks>
        private static bool HasVibratePermission(XmlElement manifest)
        {
            XmlNodeList permissions = manifest.GetElementsByTagName(UsesPermissionElement);
            foreach (XmlNode node in permissions)
            {
                var element = node as XmlElement;
                if (element == null)
                {
                    continue;
                }

                if (element.GetAttribute("name", AndroidNamespaceUri) == VibratePermission)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the <c>&lt;uses-permission&gt;</c> element declaring VIBRATE, owned by the given
        /// document but not yet attached to it.
        /// </summary>
        /// <param name="document">The loaded manifest document that will own the new element.</param>
        /// <returns>The element, ready to append to the manifest root.</returns>
        /// <remarks>
        /// The attribute is created with an explicit <c>android</c> prefix bound to the Android
        /// namespace. Unity's generated manifest always declares that prefix on the root, in which case
        /// the writer simply reuses it; if it ever does not, the declaration is emitted on the element
        /// itself, which merges identically.
        /// </remarks>
        private static XmlElement CreateVibratePermission(XmlDocument document)
        {
            XmlElement element = document.CreateElement(UsesPermissionElement);

            XmlAttribute nameAttribute = document.CreateAttribute(AndroidNamespacePrefix, "name", AndroidNamespaceUri);
            nameAttribute.Value = VibratePermission;
            element.Attributes.Append(nameAttribute);

            return element;
        }

        /// <summary>
        /// Logs an actionable error explaining that the build will ship without vibration permission.
        /// </summary>
        /// <param name="reason">Why the injection could not be performed, phrased to complete the sentence.</param>
        private static void LogInjectionFailed(string reason)
        {
            Debug.LogError(
                "[Haptics] Could not add the " + VibratePermission + " permission because " + reason +
                ". The build will install without vibration permission and every haptic will be " +
                "silently ignored on device. Declare <" + UsesPermissionElement + " android:name=\"" +
                VibratePermission + "\" /> in the project's custom main manifest to work around this.");
        }
    }
}
#endif

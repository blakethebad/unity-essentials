// The entire file is compiled only when iOS is the active build target. UnityEditor.iOS.Xcode ships
// with the iOS Build Support module, which the Editor assembly deliberately does not reference: an
// explicit reference raises a standing "Unable to resolve reference" error on any machine without
// that module (and iOS Build Support cannot be installed on Windows at all). UNITY_IOS is defined
// only when iOS is selected, which implies the module is present, so this guard trades a compile
// error on every other machine for a file that compiles to nothing there.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace UnityEssentials.Haptics.Editor
{
    /// <summary>
    /// Weak-links <c>CoreHaptics.framework</c> into the generated Xcode project so the package's
    /// native bridge can build custom haptic patterns. Runs automatically after every iOS build; no
    /// setup, no menu item, nothing for the consumer to remember.
    /// </summary>
    /// <remarks>
    /// CoreHaptics is a system framework, so nothing is bundled or copied — the link is a reference
    /// resolved on the device. The link is weak because the framework only exists from iOS 13: a
    /// strong link would refuse to launch on iOS 10 to 12, where the package still plays its three
    /// presets through UIKit and degrades patterns to a single impact.
    /// <para>
    /// The framework is added to <c>UnityFramework</c> first, because that is the target Unity
    /// compiles <c>Plugins/iOS/UnityEssentialsHaptics.mm</c> into (since Unity 2019.3). Linking only
    /// the main application target leaves the bridge's CoreHaptics symbols unresolved and fails the
    /// Xcode link step. The main target is then linked as well, defensively: it costs nothing for a
    /// system framework and covers project layouts where the bridge ends up outside the framework
    /// target.
    /// </para>
    /// <para>
    /// Failures here are reported to the console rather than thrown. A missing project file means the
    /// build did not produce the layout this postprocessor expects, and failing the whole build over a
    /// cosmetic subsystem would be a worse outcome than a loud warning.
    /// </para>
    /// </remarks>
    public sealed class HapticsIOSBuildPostprocessor : IPostprocessBuildWithReport
    {
        /// <summary>
        /// The system framework backing custom patterns. Present from iOS 13; weak-linked so older
        /// devices still launch.
        /// </summary>
        private const string CoreHapticsFramework = "CoreHaptics.framework";

        /// <summary>
        /// Runs at the default priority. Nothing this postprocessor does depends on, or interferes
        /// with, other build callbacks: it only appends a system framework reference to two targets.
        /// </summary>
        public int callbackOrder
        {
            get { return 0; }
        }

        /// <summary>
        /// Adds the weak <c>CoreHaptics.framework</c> reference to the Xcode project Unity just wrote.
        /// Does nothing for any build target other than iOS.
        /// </summary>
        /// <param name="report">
        /// The finished build's report. Its summary supplies the build target and the output directory
        /// the Xcode project was written to.
        /// </param>
        /// <remarks>
        /// The target check is defensive rather than load-bearing — <c>UNITY_IOS</c> already implies an
        /// iOS build — but it keeps the callback honest if the compilation guard is ever relaxed.
        /// </remarks>
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            string pbxProjectPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            if (!File.Exists(pbxProjectPath))
            {
                Debug.LogWarning(
                    "[Haptics] No Xcode project was found at \"" + pbxProjectPath + "\", so " +
                    CoreHapticsFramework + " was not linked. Custom haptic patterns will fail to link " +
                    "unless the framework is added to the UnityFramework target by hand.");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(pbxProjectPath);

            // The .mm compiles into UnityFramework, so this is the target that must resolve the
            // CoreHaptics symbols; the main target is linked as a harmless safety net.
            LinkCoreHaptics(project, project.GetUnityFrameworkTargetGuid());
            LinkCoreHaptics(project, project.GetUnityMainTargetGuid());

            project.WriteToFile(pbxProjectPath);
        }

        /// <summary>
        /// Weak-links <c>CoreHaptics.framework</c> into one target, skipping targets the project does
        /// not define.
        /// </summary>
        /// <param name="project">The loaded Xcode project to modify in memory.</param>
        /// <param name="targetGuid">The target to link into; ignored when null or empty.</param>
        private static void LinkCoreHaptics(PBXProject project, string targetGuid)
        {
            if (string.IsNullOrEmpty(targetGuid))
            {
                return;
            }

            project.AddFrameworkToProject(targetGuid, CoreHapticsFramework, true);
        }
    }
}
#endif

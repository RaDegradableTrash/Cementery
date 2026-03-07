#if UNITY_EDITOR && UNITY_IOS

using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
namespace Unity.UOS.Insight.Editor
{
    public class PostProcessBuild
    {
        [PostProcessBuild(88)]
        public static void OnPostProcessBuild(BuildTarget target, string targetPath)
        {
            if (target != BuildTarget.iOS)
            {
                Debug.LogWarning("Warning: Target is not iOS. XCodePostProcess will not run");
                return;
            }

            string projPath = Path.GetFullPath(targetPath) + "/Unity-iPhone.xcodeproj/project.pbxproj";

            PBXProject proj = new PBXProject();
            proj.ReadFromFile(projPath);
#if UNITY_2019_3_OR_NEWER
            string targetGuid = proj.GetUnityFrameworkTargetGuid();
#else
            string targetGuid = proj.TargetGuidByName(PBXProject.GetUnityTargetName());
#endif

            //Build Property
            proj.SetBuildProperty(targetGuid, "ENABLE_BITCODE", "NO");//BitCode  NO
            proj.SetBuildProperty(targetGuid, "GCC_ENABLE_OBJC_EXCEPTIONS", "YES");//Enable Objective-C Exceptions
            proj.AddBuildProperty(targetGuid, "OTHER_LDFLAGS", "-ObjC");

            string[] headerSearchPathsToAdd = { "$(SRCROOT)/Libraries/Plugins/iOS/TuanjieSDK/Source/main", "$(SRCROOT)/Libraries/Plugins/iOS/TuanjieSDK/Source/common" };
            proj.UpdateBuildProperty(targetGuid, "HEADER_SEARCH_PATHS", headerSearchPathsToAdd, null);// Header Search Paths

            //Add Frameworks
            proj.AddFrameworkToProject(targetGuid, "WebKit.framework", true);
            proj.AddFrameworkToProject(targetGuid, "CoreTelephony.framework", true);
            proj.AddFrameworkToProject(targetGuid, "SystemConfiguration.framework", true);
            proj.AddFrameworkToProject(targetGuid, "Security.framework", true);
            proj.AddFrameworkToProject(targetGuid, "UserNotifications.framework", true);
            proj.AddFrameworkToProject(targetGuid, "AdSupport.framework", false);
            proj.AddFrameworkToProject(targetGuid, "AppTrackingTransparency.framework", false);

            //Add Lib
            proj.AddFileToBuild(targetGuid, proj.AddFile("usr/lib/libsqlite3.tbd", "libsqlite3.tbd", PBXSourceTree.Sdk));
            proj.AddFileToBuild(targetGuid, proj.AddFile("usr/lib/libz.tbd", "libz.tbd", PBXSourceTree.Sdk));

            proj.WriteToFile(projPath);

            //Info.plist
            //Disable preset properties
            string plistPath = Path.Combine(targetPath, "Info.plist");
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSUserTrackingUsageDescription", "此标识符将用于为您提供更相关的广告内容和个性化服务。");
            plist.WriteToFile(plistPath);
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_OPENHARMONY
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace UnityEngine.DataAnalytics.Editors
{
    public class PreprocessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.OpenHarmony)
            {
                EnsureOpenHarmonyPermissions();
            }
        }

        private void EnsureOpenHarmonyPermissions()
        {
            var requiredPermissions = new string[]
            {
                "ohos.permission.APP_TRACKING_CONSENT",
            };

            var currentPermissions = PlayerSettings.OpenHarmony.openHarmonyPredefinedPermissions ?? new string[0];
            var permissionsList = new List<string>(currentPermissions);
            bool changed = false;

            foreach (var permission in requiredPermissions)
            {
                if (!permissionsList.Contains(permission))
                {
                    permissionsList.Add(permission);
                    changed = true;
                    Debug.Log($"Added OpenHarmony permission: {permission}");
                }
            }

            if (changed)
            {
                PlayerSettings.OpenHarmony.openHarmonyPredefinedPermissions = permissionsList.ToArray();
            }
        }
    }
}
#endif

#if UNITY_EDITOR && UNITY_ANDROID && UNITY_2019_1_OR_NEWER
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;
using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace UnityEngine.DataAnalytics.Editors
{

    class PostProcessBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder { get { return 0; } }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (EditorUserBuildSettings.selectedBuildTargetGroup != BuildTargetGroup.Android)
                return;

            string gradleSettingsPath = path + "/../settings.gradle";
            if (File.Exists(gradleSettingsPath))
            {
                string fileContent = File.ReadAllText(gradleSettingsPath);
                var insertContent = "       maven { url 'https://jitpack.io' }\n" +
                    "       maven { url 'https://developer.huawei.com/repo' }\n" +
                    "       maven { url 'https://developer.hihonor.com/repo' }\n";

                if (fileContent.IndexOf(insertContent) >= 0)
                {
                    return;
                }

                var depText = "mavenCentral()\n";
                var index = fileContent.LastIndexOf(depText);
                if (index >= 0)
                {
                    fileContent = fileContent.Insert(index + depText.Length, insertContent);
                }

                File.WriteAllText(gradleSettingsPath, fileContent);
            }

            var buildGradlePath = GetGradlePath(path);
            if (File.Exists(buildGradlePath))
            {
                string fileContent = File.ReadAllText(buildGradlePath);
                var insertContent = "    implementation 'com.github.gzu-liyujiang:Android_CN_OAID:4.2.11' \n";

                if (fileContent.IndexOf(insertContent) >= 0)
                {
                    return;
                }

                var depText = "dependencies {\n";
                var index = fileContent.IndexOf(depText);
                if (index >= 0)
                {
                    fileContent = fileContent.Insert(index + depText.Length, insertContent);
                }

                File.WriteAllText(buildGradlePath, fileContent);
            }
        }

        private string _manifestFilePath;
        private string GetGradlePath(string basePath)
        {
            if (string.IsNullOrEmpty(_manifestFilePath))
            {
                _manifestFilePath = Path.Combine(basePath, "build.gradle");
            }

            return _manifestFilePath;
        }
    }
}
#endif

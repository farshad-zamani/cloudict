using System.Reflection;

namespace Cloudict
{
    /// <summary>
    /// Product identity read straight from the assembly, so the version shown in the UI can never
    /// drift from the version that was actually built. <c>AssemblyInfo.cs</c> is the single source
    /// of truth; nothing here needs editing on a release.
    /// </summary>
    public static class AppInfo
    {
        /// <summary>Three-part product version, e.g. <c>2.3.1</c> (the assembly's trailing revision is dropped).</summary>
        public static string Version
        {
            get
            {
                // The *entry* assembly, not the executing one: this type now lives in
                // Cloudict.Core.dll, and asking that library for its version reports the library's
                // own (1.0.0) rather than the application's.
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var v = assembly.GetName().Version;
                return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        /// <summary>The version as shown beside the app name, e.g. <c>v2.3.1</c>.</summary>
        public static string DisplayVersion => "v" + Version;
    }
}

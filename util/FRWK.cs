using System;
using System.Reflection;
using System.Runtime.Versioning;

namespace OrderEmail.util
{
    public class FRWK
    {
        public static string GetTargetFrameworkName()
        {
            return Assembly
                .GetEntryAssembly()?
                .GetCustomAttribute<TargetFrameworkAttribute>()?
                .FrameworkName;
        }

        public static string GetEnvironmentVersion()
        {
            return Environment.Version.ToString();
        }

        public static string GetFrameworkDescription()
        {
            return System
                .Runtime
                .InteropServices
                .RuntimeInformation
                .FrameworkDescription;
        }
    }
}

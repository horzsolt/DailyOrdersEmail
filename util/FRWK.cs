using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace DailyOrdersEmail.util
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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DailyOrdersEmail
{
    public static class Util
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public static void SaveStringBuilderToFile(StringBuilder sb, string filePath)
        {
            log.Debug($"Saving to file: {filePath}");
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.Write(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                log.Error($"An error occurred while saving to file: {ex.Message}");
            }
        }

        public static T GetValueOrDefault<T>(object value)
        {
            if (value == DBNull.Value || value == null)
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)string.Empty;
                }
                else if (typeof(T) == typeof(int))
                {
                    return (T)(object)0;
                }
                else if (typeof(T) == typeof(DateTime))
                {
                    return (T)(object)DateTime.Now;
                }
                else
                {
                    return default(T);
                }
            }

            return (T)value;
        }

    }
}

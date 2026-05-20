using System.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Threading;
using log4net;
using log4net.Repository.Hierarchy;

namespace OrderEmail.util
{
    public static class Util
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static DataTable GetDataWithRetry(
            SqlConnection connection,
            string query,
            Action<SqlCommand> parameterize,
            int maxRetries = 3)
        {
            int attempt = 0;

            while (true)
            {
                try
                {
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        parameterize(command);
                        command.CommandTimeout = 0;

                        log.Info(
                            $"Executing SQL (attempt {attempt + 1}/{maxRetries})");

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            DataTable dataTable = new DataTable();
                            dataTable.Load(reader);
                            return dataTable;
                        }
                    }
                }
                catch (SqlException ex) when (IsTransientSqlError(ex))
                {
                    attempt++;

                    if (attempt >= maxRetries)
                    {
                        log.Error("Max retry reached");
                        log.Error(ex);
                        throw;
                    }

                    log.Warn($"Transient SQL error (attempt {attempt}/{maxRetries}). Retrying in 120 seconds...");
                    log.Warn(ex);
                    Thread.Sleep(TimeSpan.FromMinutes(2));
                }
            }
        }

        public static bool IsTransientSqlError(SqlException ex)
        {
            foreach (SqlError error in ex.Errors)
            {
                if (error.Number == -2   // Timeout
                    || error.Number == 1205 // Deadlock
                    || error.Number == 1222) // Lock request timeout
                {
                    return true;
                }
            }

            return false;
        }

        public static Configuration LoadConfiguration(SqlConnection connection)
        {
            Configuration config = new Configuration();
            config.TestMode =
                bool.TryParse(
                    Environment.GetEnvironmentVariable("VIR_TEST_MODE"),
                    out bool testMode)
                && testMode;

            string query = "SELECT TOP 1 * FROM dbo.DailyOrderMailConfig";

            using (SqlCommand command = new SqlCommand(query, connection))
            using (SqlDataReader reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    config.LastCheckTime = reader.IsDBNull(0) ? DateTime.Now.AddHours(-1) : reader.GetDateTime(0);
                    config.MailServer = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                    config.MailSecrecy = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                    config.MailPassword = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                    config.MailSendTo = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim();
                    config.MailSaveToFolder = reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Trim();
                    config.MailSelectStatement = reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim();
                    config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                    config.MailSendFrom = reader.IsDBNull(8) ? string.Empty : reader.GetString(8).Trim();
                }
                else
                {
                    throw new Exception("No records found in DailyOrderMailConfig");
                }
            }

            return config;
        }

        public static string RemoveSpecialCharsFromDateTime(DateTime dateTime)
        {
            string dateTimeString = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            string cleanedString = Regex.Replace(dateTimeString, @"[^0-9a-zA-Z]+", "");
            return cleanedString;
        }

        public static string GetLogDirectory()
        {
            var hierarchy = (Hierarchy)LogManager.GetRepository();

            var appender = hierarchy.Root.Appenders
                .OfType<log4net.Appender.RollingFileAppender>()
                .FirstOrDefault(a => a.Name == "FileAppender");

            if (appender == null || string.IsNullOrEmpty(appender.File))
                throw new InvalidOperationException("FileAppender not found or not configured.");

            return Path.GetDirectoryName(appender.File);
        }
        public static void RemoveOldFiles()
        {
            try
            {
                string folderPath = GetLogDirectory();
                int daysOld = 10;

                log.Debug($"Removing files older than {daysOld} days from: {folderPath}");

                DateTime currentDate = DateTime.Now;
                string[] files = Directory.GetFiles(folderPath);

                foreach (string file in files)
                {
                    if (Path.GetFileName(file) == "vir_daily_orders_email_.log")
                    {
                        continue;
                    }

                    DateTime creationTime = File.GetCreationTime(file);
                    TimeSpan fileAge = currentDate - creationTime;

                    if (fileAge.TotalDays > daysOld)
                    {
                        File.Delete(file);
                        log.Debug($"Deleted: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"An error occurred: {ex}");
            }
        }

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

        public static void SendEmail(string htmlContent, Configuration config, string subject, string sumAmount, string sendTo = null)
        {
            if (config.TestMode == true)
            {
                log.Debug("Email sending to the default user in test mode.");
                sendTo = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_USERNAME");
            }

            /*log.Debug("Email sending is disabled.");
            return;
            */

            try
            {
                log.Debug($"Sending email: {subject} using {config.MailServer}:587");
                MailMessage mail = new MailMessage();
                SmtpClient smtpClient = new SmtpClient(config.MailServer);

                mail.From = new MailAddress(config.MailSendFrom);
                mail.To.Add(sendTo == null ? config.MailSendTo : sendTo);
                mail.Subject = $"{subject} [Σ: {sumAmount}]";
                mail.IsBodyHtml = true;
                mail.Body = htmlContent;

                smtpClient.Port = 587;
                smtpClient.Credentials = new NetworkCredential(config.MailSendFrom, config.MailPassword);
                smtpClient.EnableSsl = true;

                smtpClient.Send(mail);
                log.Debug($"Email sent successfully: {subject}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to send email: {ex}");
                throw;
            }
        }
    }
}

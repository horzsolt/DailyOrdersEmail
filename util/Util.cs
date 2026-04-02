using System.Linq;
using log4net;
using log4net.Repository.Hierarchy;
using log4net.Appender;
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace OrderEmail.util
{
    public static class Util
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

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
                .OfType<RollingFileAppender>()
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

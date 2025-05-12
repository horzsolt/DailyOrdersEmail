using log4net;
using System;
using System.IO;
using System.Net.Mail;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DailyOrdersEmail.util
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
        public static void RemoveOldFiles(string folderPath, int daysOld)
        {
            try
            {
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
            if (config.TestMode == false)
            {
                log.Debug("Email sending is disabled in test mode.");
                return;
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

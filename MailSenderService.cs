using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Timers;
using log4net;
using System.Reflection;
using System.ServiceProcess;
using System.Diagnostics;
using System.Net;

namespace DailyOrdersEmail
{
    public class MailSenderService : ServiceBase
    {
        private Timer checkTimer;
        private DateTime lastRunningTime;
        private readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public MailSenderService()
        {
            
        }

        // Custom start method for running in console
        public void StartAsConsole(string[] args)
        {
            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                log.Error($"Error: {ex}");
            }
        }

        protected override void OnStart(string[] args)
        {
            log.Debug("Service OnStart called.");
            checkTimer = new Timer(1800000);
            //checkTimer = new Timer(300000);
            checkTimer.Elapsed += StartCheck_Scheduled;
            checkTimer.AutoReset = true;
            checkTimer.Enabled = true;
        }

        protected override void OnStop()
        {
            log.Info("Service OnStop called.");
        }

        private void StartCheck_Scheduled(object source, ElapsedEventArgs e)
        {
            log.Info("Scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                log.Error($"Error: {ex}");
            }

            log.Info("Scheduled run finished.");
            stopwatch.Stop();
            log.Info($"Elapsed Time: {stopwatch.Elapsed.Hours} hours, {stopwatch.Elapsed.Minutes} minutes, {stopwatch.Elapsed.Seconds} seconds");
        }

        private void CheckForNewRecords()
        {

            string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                                      $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                                      $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                                      $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};" +
                                      "Connection Timeout=500;";

            log.Debug($"Connection string: {connectionString}");
            
            Configuration config = new Configuration();
            config.TestMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VIR_TEST_MODE"));

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT TOP 1 * FROM dbo.DailyOrderMailConfig";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            config.LastCheckTime = reader.IsDBNull(0) ? (DateTime.Now.AddHours(-1)) : reader.GetDateTime(0);
                            config.MailServer = reader.IsDBNull(1) ? String.Empty : reader.GetString(1).Trim();
                            config.MailSecrecy = reader.IsDBNull(2) ? String.Empty : reader.GetString(2).Trim();
                            config.MailPassword = reader.IsDBNull(3) ? String.Empty : reader.GetString(3).Trim();
                            config.MailSendTo = reader.IsDBNull(4) ? String.Empty : reader.GetString(4).Trim();
                            config.MailSaveToFolder = reader.IsDBNull(5) ? String.Empty : reader.GetString(5).Trim();
                            config.MailSelectStatement = reader.IsDBNull(6) ? String.Empty : reader.GetString(6).Trim();
                            config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                            config.MailSendFrom = reader.IsDBNull(8) ? String.Empty : reader.GetString(8).Trim();

                            if (config.TestMode == true)
                            {
                                config.LastCheckTime = DateTime.Now.AddHours(-1);
                            }

                            log.Debug($"Last check time: {config.LastCheckTime}");
                            log.Debug($"Select stmt: {config.MailSelectStatement}");
                            log.Debug($"SaveTo folder: {config.MailSaveToFolder}");
                        }
                        else
                        {
                            log.Error("No records found in the DailyOrderMailConfig table.");
                            return;
                        }
                    }
                }

                query = config.MailSelectStatement;
                lastRunningTime = DateTime.Now;

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 500;
                    command.Parameters.AddWithValue("@timestamp", config.LastCheckTime);

                    log.Debug($"Executing query: {command.CommandText}");

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Load(reader);
                        GenerateHtmlEmail(dataTable, config);
                    }
                }

                if (config.TestMode == false)
                {

                    query = "UPDATE dbo.DailyOrderMailConfig SET last_check = @timestamp";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@timestamp", lastRunningTime);
                        int rowsAffected = command.ExecuteNonQuery();
                        log.Debug($"Rows affected after updating the lastCheckTime: {rowsAffected}");
                    }

                    Util.RemoveOldFiles(config.MailSaveToFolder, 30);
                } else
                {
                    log.Debug("Running in test mode, no changes will be saved to the database.");
                }
            }
        }

        private void GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.Info("No records found.");
                return;
            }

            int CID = dataTable.Rows[0].Field<int>("CID");
            string agentName = dataTable.Rows[0].Field<string>("Nev");

            int actual_CID = 0;
            string actual_agentName = String.Empty;

            int sum_Rend_Unit = 0;
            int sum_Rabatt = 0;
            double sum_Forgalom = 0;

            StringBuilder htmlBuilder = new StringBuilder();
            AddHeader(htmlBuilder, dataTable.Rows[0]);

            foreach (DataRow row in dataTable.Rows)
            {
                actual_CID = row["CID"] is DBNull ? 0 : row.Field<int>("CID");
                actual_agentName = row["Nev"] is DBNull ? String.Empty : row.Field<string>("Nev");

                int index = dataTable.Rows.IndexOf(row);

                log.Debug($"Actual_CID: {actual_CID}");
                log.Debug($"Actual_AgentName: {actual_agentName}");
                log.Debug($"{index.ToString()}/{dataTable.Rows.Count}");

                if ((actual_CID != CID) || (actual_agentName != agentName))
                {
                    // Add summary
                    log.Debug($"Add summary for: {CID}, {agentName}. Row index: {index.ToString()}/{dataTable.Rows.Count}");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{string.Format("{0:C0}", sum_Forgalom)}</b></td>");
                    htmlBuilder.Append("</tr>");
                    htmlBuilder.Append("</table>");

                    AddFooter(htmlBuilder);

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}";

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject);
                    htmlBuilder.Clear();

                    CID = actual_CID;
                    agentName = actual_agentName;
                    sum_Forgalom = 0;
                    sum_Rabatt = 0;
                    sum_Rend_Unit = 0;

                    AddHeader(htmlBuilder, row);
                }

                if (index == dataTable.Rows.Count - 1)
                {
                    // Add summary
                    log.Debug($"[END]: Add summary for: {CID}, {agentName}. Row index: {index.ToString()}/{dataTable.Rows.Count}");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td align='left'>{row["Termek"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rabatt"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                    htmlBuilder.Append("</tr>");

                    sum_Rend_Unit += row.Field<int>("Rend_Unit");
                    sum_Rabatt += row.Field<int>("Rabatt");
                    sum_Forgalom += row.Field<double>("Forgalom");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{string.Format("{0:C0}", sum_Forgalom)}</b></td>");
                    htmlBuilder.Append("</tr>");
                    htmlBuilder.Append("</table>");

                    AddFooter(htmlBuilder);

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}";

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject);

                    htmlBuilder.Clear();
                }

                htmlBuilder.Append("<tr>");
                htmlBuilder.Append($"<td align='left'>{row["Termek"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rabatt"]}</td>");
                htmlBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                htmlBuilder.Append("</tr>");

                sum_Rend_Unit += row.Field<int>("Rend_Unit");
                sum_Rabatt += row.Field<int>("Rabatt");
                sum_Forgalom += row.Field<double>("Forgalom");
            }
        }

        private void AddFooter(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("<br><br>");
            htmlBuilder.Append("<p style='font-family: Arial, sans-serif; font-size: 9px; color: #333;'>");
            htmlBuilder.Append("<a href='mailto:jane.doe@example.com'>jane.doe@example.com</a> | Írj, ha hibát találsz.<br>");
            htmlBuilder.Append("Ez az üzenet automatikusan generálódott, kérjük ne válaszolj rá.<br>");
            htmlBuilder.Append("</p>");
            htmlBuilder.Append("</html>");
        }

        private void AddHeader(StringBuilder htmlBuilder, DataRow row)
        {
            log.Debug($"Add header for {row.Field<string>("Nev")}");

            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");
            htmlBuilder.Append("<table border='1' style='border-collapse: collapse;padding: 2px;'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Rendelés ideje</td>");
            htmlBuilder.Append($"<td>{row.Field<DateTime>("Rogzitve")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Név</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Nev")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>CID</td>");
            htmlBuilder.Append($"<td align='right'>{row.Field<int>("CID")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Ügyfél</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Ugyfel")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Helység</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Helyseg")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Nagyker</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Nagyker")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("</table>");

            htmlBuilder.Append("<table border='1' style='border-collapse: collapse;padding: 2px;'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<th>Termék</th>");
            htmlBuilder.Append($"<th>Kedv. %</th>");
            htmlBuilder.Append($"<th>Rendelt menny.</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
        }

        private void SendEmail(string htmlContent, Configuration config, string subject)
        {
            if (config.TestMode == true)
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
                mail.To.Add("horzsolt2006@gmail.com");
                mail.To.Add(config.MailSendTo);
                mail.Subject = subject;
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

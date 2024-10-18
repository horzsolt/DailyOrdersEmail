using System;
using System.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Timers;
using log4net;
using System.Reflection;
using System.Web.UI.WebControls;

namespace DailyOrdersEmail
{
    public class NewOrdersHandler
    {
        private DateTime? lastCheckedTimestamp;
        private readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public void StartCheck_Scheduled(object source, ElapsedEventArgs e)
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
                                config.LastCheckTime = DateTime.Now.AddHours(-2);
                            }

                            log.Debug($"Last check time: {config.LastCheckTime}");
                        }
                        else
                        {
                            log.Error("No records found in the DailyOrderMailConfig table.");
                            return;
                        }
                    }
                }

                query = config.MailSelectStatement;

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 500;
                    command.Parameters.AddWithValue("@timestamp", config.LastCheckTime);

                    string loggableQuery = query.Replace("@timestamp", $"'{config.LastCheckTime:yyyy-MM-dd HH:mm:ss}'");
                    log.Debug($"Executing query: {loggableQuery}");

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Load(reader);
                        lastCheckedTimestamp = GenerateHtmlEmail(dataTable, config);
                    }
                }

                if (config.TestMode == false)
                {
                    if (lastCheckedTimestamp.HasValue)
                    {
                        log.Info($"Updating the lastCheckTime in the database to {lastCheckedTimestamp}");

                        query = "UPDATE dbo.DailyOrderMailConfig SET last_check = @timestamp";

                        using (SqlCommand command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@timestamp", lastCheckedTimestamp);
                            int rowsAffected = command.ExecuteNonQuery();
                            log.Debug($"Rows affected after updating the lastCheckTime: {rowsAffected}");
                        }
                    }
                    else
                    {
                        log.Info("As no new orders found the last_check value has not been changed.");
                    }

                    Util.RemoveOldFiles(config.MailSaveToFolder, 30);
                }
                else
                {
                    log.Debug("Running in test mode, no changes will be saved to the database.");
                }
            }
        }

        private DateTime? GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.Info("No records found.");
                return null;
            }

            log.Debug($"Found {dataTable.Rows.Count} records.");
            DataRow lastRow = dataTable.Rows[dataTable.Rows.Count - 1];
            DateTime? lastCheckedTimestamp = lastRow["Rogzitve"] != DBNull.Value ? (DateTime?)lastRow["Rogzitve"] : null;

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

                    htmlBuilder.Append("<tfoot>");
                    htmlBuilder.Append($"<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit} db</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt} db</b></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{string.Format("{0:C0}", sum_Forgalom)}</b></td>");
                    htmlBuilder.Append("</tr>");
                    htmlBuilder.Append("</tfoot>");
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

                    htmlBuilder.Append($"<tr>");
                    htmlBuilder.Append($"<td align='right'>{row["Termek"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]} db</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rabatt"]} db</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]} %</td>");
                    htmlBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                    htmlBuilder.Append("</tr>");

                    sum_Rend_Unit += row.Field<int>("Rend_Unit");
                    sum_Rabatt += row.Field<int>("Rabatt");
                    sum_Forgalom += row.Field<double>("Forgalom");

                    htmlBuilder.Append("<tfoot>");
                    htmlBuilder.Append($"<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit} db</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt} db</b></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{string.Format("{0:C0}", sum_Forgalom)}</b></td>");
                    htmlBuilder.Append("</tr>");
                    htmlBuilder.Append("</tfoot>");
                    htmlBuilder.Append("</table>");

                    AddFooter(htmlBuilder);

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}";

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject);

                    htmlBuilder.Clear();
                }

                htmlBuilder.Append($"<tr>");
                htmlBuilder.Append($"<td align='right'>{row["Termek"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]} db</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rabatt"]} db</td>");
                htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]} %</td>");
                htmlBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                htmlBuilder.Append("</tr>");

                sum_Rend_Unit += row.Field<int>("Rend_Unit");
                sum_Rabatt += row.Field<int>("Rabatt");
                sum_Forgalom += row.Field<double>("Forgalom");
            }

            return lastCheckedTimestamp;
        }

        private void AddFooter(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("<br>");
            htmlBuilder.Append("<p style='font-family: Arial, sans-serif; font-size: 10px; color: #333;'>");
            htmlBuilder.Append("Hibabejelentés, észrevétel, javaslat: <a href='mailto:horvath.zsolt@goodwillpharma.com'>horvath.zsolt@goodwillpharma.com</a><br>");
            htmlBuilder.Append("Ezt az üzenetet a VIR Rendelés Értesítő alkalmazás generálta, kérjük ne válaszolj rá.<br>");
            htmlBuilder.Append("</p>");
            htmlBuilder.Append("</html>");
        }

        private void AddHeader(StringBuilder htmlBuilder, DataRow row)
        {
            log.Debug($"Add header for {row.Field<string>("Nev")}");

            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");

            htmlBuilder.Append("<head>");
            htmlBuilder.Append("<style>");
            htmlBuilder.Append("table {border: none;} ");
            htmlBuilder.Append("th { background: #6BAFBC; padding: .75pt .75pt .75pt .75pt; font-size:8.5pt;font-family:'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in;margin-bottom:15.0pt;margin-left:0in; } ");
            htmlBuilder.Append("td { background: #FFFCF2; padding: 1.5pt 3.75pt 1.5pt 3.75pt; font-size:8.5pt;font-family: 'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in; margin-bottom:15.0pt; margin-left:0in;line-height:12.0pt } ");
            htmlBuilder.Append("tf { background: #6BAFBC; padding: .75pt .75pt .75pt .75pt; font-size:8.5pt;font-family:'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in;margin-bottom:15.0pt;margin-left:0in; } ");
            htmlBuilder.Append("</style>");
            htmlBuilder.Append("</head>");

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");
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
            htmlBuilder.Append($"<td align='left'>{row.Field<int>("CID")}</td>");
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

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");
            htmlBuilder.Append($"<tr>");
            htmlBuilder.Append($"<th>Termék</th>");
            htmlBuilder.Append($"<th>Rendelt menny.</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Kedv. %</th>");
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

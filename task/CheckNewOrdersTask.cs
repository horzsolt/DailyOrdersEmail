using System;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Data;
using Microsoft.Extensions.Logging;
using DailyOrdersEmail.services;
using DailyOrdersEmail.util;

namespace DailyOrdersEmail.task
{
    public class CheckNewOrdersTask(MetricService metricService, ILogger<CheckNewOrdersTask> log) : ServiceTask
    {
        private DateTime? lastCheckedTimestamp;

        public void ExecuteTask()
        {
            log.LogInformation("Scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.JobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.JobExecutionStatus = 1;
            metricService.RecordJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
            log.LogInformation($"Elapsed Time: {stopwatch.Elapsed.Hours} hours, {stopwatch.Elapsed.Minutes} minutes, {stopwatch.Elapsed.Seconds} seconds");
        }

        private void CheckForNewRecords()
        {

            string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                                      $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                                      $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                                      $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};" +
                                      "Connection Timeout=500;Trust Server Certificate=true";

            log.LogDebug($"Connection string: {connectionString}");

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
                            config.LastCheckTime = reader.IsDBNull(0) ? DateTime.Now.AddHours(-1) : reader.GetDateTime(0);
                            config.MailServer = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                            config.MailSecrecy = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                            config.MailPassword = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                            config.MailSendTo = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim();
                            config.MailSaveToFolder = reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Trim();
                            config.MailSelectStatement = reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim();
                            config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                            config.MailSendFrom = reader.IsDBNull(8) ? string.Empty : reader.GetString(8).Trim();

                            if (config.TestMode == true)
                            {
                                config.LastCheckTime = DateTime.Now.AddHours(-2);
                            }

                            log.LogDebug($"Last check time: {config.LastCheckTime}");
                        }
                        else
                        {
                            metricService.JobExecutionStatus = 0;
                            log.LogError("No records found in the DailyOrderMailConfig table.");
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
                    log.LogDebug($"Executing query: {loggableQuery}");

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
                        log.LogInformation($"Updating the lastCheckTime in the database to {lastCheckedTimestamp}");

                        query = "UPDATE dbo.DailyOrderMailConfig SET last_check = @timestamp";

                        using (SqlCommand command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@timestamp", lastCheckedTimestamp);
                            int rowsAffected = command.ExecuteNonQuery();
                            log.LogDebug($"Rows affected after updating the lastCheckTime: {rowsAffected}");
                        }
                    }
                    else
                    {
                        log.LogInformation("As no new orders found the last_check value has not been changed.");
                    }

                    Util.RemoveOldFiles(config.MailSaveToFolder, 10);
                }
                else
                {
                    log.LogDebug("Running in test mode, no changes will be saved to the database.");
                }
            }
        }

        private DateTime? GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.LogInformation("No records found.");
                return null;
            }

            log.LogDebug($"Found {dataTable.Rows.Count} records.");
            DataRow lastRow = dataTable.Rows[dataTable.Rows.Count - 1];
            DateTime? lastCheckedTimestamp = lastRow["Rogzitve"] != DBNull.Value ? (DateTime?)lastRow["Rogzitve"] : null;

            int CID = dataTable.Rows[0].Field<int>("CID");
            string agentName = dataTable.Rows[0].Field<string>("Nev");
            string nagyker = dataTable.Rows[0].Field<string>("Nagyker");

            int actual_CID = 0;
            string actual_agentName = string.Empty;
            string actual_Nagyker = string.Empty;

            int sum_Rend_Unit = 0;
            int sum_Rabatt = 0;
            double sum_Turnover = 0;
            double overall_Turnover = 0;
            int overall_OrderCount = 0;

            StringBuilder htmlTableBuilder = new StringBuilder();
            StringBuilder htmlBuilder = new StringBuilder();
            AddHeader(htmlBuilder, dataTable.Rows[0]);
            int orderCounter = 0;

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;

                actual_CID = row["CID"] is DBNull ? 0 : row.Field<int>("CID");
                actual_agentName = row["Nev"] is DBNull ? string.Empty : row.Field<string>("Nev");
                actual_Nagyker = row["Nagyker"] is DBNull ? string.Empty : row.Field<string>("Nagyker");

                int index = dataTable.Rows.IndexOf(row);

                log.LogDebug($"Actual_CID: {actual_CID}");
                log.LogDebug($"Actual_AgentName: {actual_agentName}");
                log.LogDebug($"Actual_Nagyker: {actual_Nagyker}");
                log.LogDebug($"{index.ToString()}/{dataTable.Rows.Count}");

                if (actual_CID != CID || actual_agentName != agentName || actual_Nagyker != nagyker)
                {
                    // Add summary
                    log.LogDebug($"Add summary for: {CID}, {agentName}, {nagyker}. Row index: {index.ToString()}/{dataTable.Rows.Count}");

                    htmlTableBuilder.Append("<tfoot>");
                    if (orderCounter > 10)
                    {
                        InsertSummary(sum_Rend_Unit, sum_Rabatt, sum_Turnover, htmlTableBuilder);
                    }
                    htmlTableBuilder.Append("</tfoot>");
                    htmlTableBuilder.Append("</table>");

                    AddFooter(htmlTableBuilder);

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}";

                    InsertSummary(sum_Rend_Unit, sum_Rabatt, sum_Turnover, htmlBuilder);
                    htmlBuilder.Append(htmlTableBuilder.ToString());

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", sum_Turnover));
                    overall_OrderCount++;

                    htmlTableBuilder.Clear();
                    htmlBuilder.Clear();

                    CID = actual_CID;
                    agentName = actual_agentName;
                    nagyker = actual_Nagyker;
                    sum_Turnover = 0;
                    sum_Rabatt = 0;
                    sum_Rend_Unit = 0;
                    orderCounter = 0;

                    AddHeader(htmlBuilder, row);
                }

                if (index == dataTable.Rows.Count - 1)
                {
                    // Add summary
                    log.LogDebug($"[END]: Add summary for: {CID}, {agentName}, {nagyker}. Row index: {index.ToString()}/{dataTable.Rows.Count}");

                    htmlTableBuilder.Append($"<tr class='lowertabletr'>");
                    htmlTableBuilder.Append($"<td align='right'>{row["Termek"]}</td>");
                    htmlTableBuilder.Append($"<td align='right'>{row["Rend_Unit"]} db</td>");
                    htmlTableBuilder.Append($"<td align='right'>{row["Rabatt"]} db</td>");
                    htmlTableBuilder.Append($"<td align='right'>{row["Kedv_Sz"]} %</td>");
                    htmlTableBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                    htmlTableBuilder.Append("</tr>");

                    sum_Rend_Unit += row.Field<int>("Rend_Unit");
                    sum_Rabatt += row.Field<int>("Rabatt");
                    sum_Turnover += row.Field<double>("Forgalom");
                    overall_Turnover += sum_Turnover;

                    htmlTableBuilder.Append("<tfoot>");
                    if (orderCounter > 10)
                    {
                        InsertSummary(sum_Rend_Unit, sum_Rabatt, sum_Turnover, htmlTableBuilder);
                    }
                    htmlTableBuilder.Append("</tfoot>");
                    htmlTableBuilder.Append("</table>");

                    AddFooter(htmlTableBuilder);

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}";

                    InsertSummary(sum_Rend_Unit, sum_Rabatt, sum_Turnover, htmlBuilder);
                    htmlBuilder.Append(htmlTableBuilder.ToString());

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", sum_Turnover));
                    overall_OrderCount++;

                    htmlTableBuilder.Clear();
                    htmlBuilder.Clear();
                }

                htmlTableBuilder.Append($"<tr class='lowertabletr'>");
                htmlTableBuilder.Append($"<td align='right'>{row["Termek"]}</td>");
                htmlTableBuilder.Append($"<td align='right'>{row["Rend_Unit"]} db</td>");
                htmlTableBuilder.Append($"<td align='right'>{row["Rabatt"]} db</td>");
                htmlTableBuilder.Append($"<td align='right'>{row["Kedv_Sz"]} %</td>");
                htmlTableBuilder.Append($"<td align='right'>{string.Format("{0:C0}", row["Forgalom"])}</td>");
                htmlTableBuilder.Append("</tr>");

                sum_Rend_Unit += row.Field<int>("Rend_Unit");
                sum_Rabatt += row.Field<int>("Rabatt");
                sum_Turnover += row.Field<double>("Forgalom");
                overall_Turnover += sum_Turnover;
            }

            metricService.OrderSum = overall_Turnover;
            metricService.OrderCount = overall_OrderCount;
            return lastCheckedTimestamp;
        }

        private static void InsertSummary(int sum_Rend_Unit, int sum_Rabatt, double sum_Forgalom, StringBuilder htmlBuilder)
        {
            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<td align='center'><b>Σ</b></td>");
            htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit} db</b></td>");
            htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt} db</b></td>");
            htmlBuilder.Append($"<td></td>");
            htmlBuilder.Append($"<td align='right'><b>{string.Format("{0:C0}", sum_Forgalom)}</b></td>");
            htmlBuilder.Append("</tr>");
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
            log.LogDebug($"Add header for {row.Field<string>("Nev")}");

            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");

            htmlBuilder.Append("<head>");
            htmlBuilder.Append("<style>");
            htmlBuilder.Append(".uppertable {border: none;} ");
            htmlBuilder.Append("th { background: #6BAFBC; font-size:8.5pt;font-family:'Arial',sans-serif; padding: 1pt 1pt 1pt 1pt;} ");
            htmlBuilder.Append("td { border: none; background: #FFFCF2; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; } ");
            htmlBuilder.Append(".lowertabletr { border: none; height: 30px;padding: 1pt 1pt 1pt 1pt; } ");
            htmlBuilder.Append(".simpletd { border: none; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; background: #FFFCF2; color:#333333;} ");
            htmlBuilder.Append("tf { background: #6BAFBC; padding: .75pt .75pt .75pt .75pt; font-size:8.5pt;font-family:'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in;margin-bottom:15.0pt;margin-left:0in; } ");
            htmlBuilder.Append("</style>");
            htmlBuilder.Append("</head>");

            htmlBuilder.Append($"<table class='uppertable'>");
            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td>Rendelés ideje</td>");
            htmlBuilder.Append($"<td>{row.Field<DateTime>("Rogzitve")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'>Név</td>");
            htmlBuilder.Append($"<td class='simpletd'>{row.Field<string>("Nev")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'>CID</td>");
            htmlBuilder.Append($"<td align='left' class='simpletd'>{row.Field<int>("CID")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px''>");
            htmlBuilder.Append($"<td class='simpletd'>Ügyfél</td>");
            htmlBuilder.Append($"<td class='simpletd'>{row.Field<string>("Ugyfel")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'>Helység</td>");
            htmlBuilder.Append($"<td class='simpletd'>{row.Field<string>("Helyseg")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'>Nagyker</td>");
            htmlBuilder.Append($"<td class='simpletd'>{row.Field<string>("Nagyker")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("</table>");

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");
            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<th>Termék</th>");
            htmlBuilder.Append($"<th>Menny.</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Kedv. %</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
        }

        private void SendEmail(string htmlContent, Configuration config, string subject, string sumAmount)
        {
            if (config.TestMode == true)
            {
                log.LogDebug("Email sending is disabled in test mode.");
                return;
            }

            try
            {
                log.LogDebug($"Sending email: {subject} using {config.MailServer}:587");
                MailMessage mail = new MailMessage();
                SmtpClient smtpClient = new SmtpClient(config.MailServer);

                mail.From = new MailAddress(config.MailSendFrom);
                mail.To.Add(config.MailSendTo);
                mail.Subject = $"{subject} [Σ: {sumAmount}]";
                mail.IsBodyHtml = true;
                mail.Body = htmlContent;

                smtpClient.Port = 587;
                smtpClient.Credentials = new NetworkCredential(config.MailSendFrom, config.MailPassword);
                smtpClient.EnableSsl = true;

                smtpClient.Send(mail);
                log.LogDebug($"Email sent successfully: {subject}");
            }
            catch (Exception ex)
            {
                log.LogError($"Failed to send email: {ex}");
                throw;
            }
        }
    }
}

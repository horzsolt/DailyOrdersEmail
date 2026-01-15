using DailyOrdersEmail.services;
using DailyOrdersEmail.util;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DailyOrdersEmail.task
{
    [DailyOrderSummaryTask]
    public class DailyScriptorOrderSummaryTask(MetricService metricService, ILogger<DailyScriptorOrderSummaryTask> log) : ServiceTask
    {
        public void ExecuteTask()
        {
            log.LogInformation("5pm Scriptor summary email sender scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.DailyScriptorOrderSummaryJobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.DailyScriptorOrderSummaryJobExecutionStatus = 1;
            metricService.RecordDailyScriptorOrderSummaryJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
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
                            config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                            config.MailSendFrom = reader.IsDBNull(8) ? string.Empty : reader.GetString(8).Trim();
                        }
                        else
                        {
                            metricService.DailyOrderSummaryJobExecutionStatus = 0;
                            log.LogError("No records found in the DailyOrderMailConfig table.");
                            return;
                        }
                    }
                }

                //DateTime queryDate = new DateTime(2025, 10, 17);
                DateTime queryDate = DateTime.Today;
                DateTime tomorrow = queryDate.AddDays(1);


                int sumRabatt = 0;
                int sumRendUnit = 0;
                double sumForgalom = 0;

                query = @"
                        SELECT SUM(Rabatt) Rabatt, SUM(Rend_Unit) Unit, SUM(Forgalom) Forgalom
                        FROM [vir].[dbo].[v_rendeles_teteles_u2]
                        WHERE Rogzitve >= @StartDay AND Rogzitve < @EndDay;";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.Add("@StartDay", SqlDbType.Date).Value = queryDate;
                    command.Parameters.Add("@EndDay", SqlDbType.Date).Value = tomorrow;
                    command.CommandTimeout = 500;
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            sumRabatt = reader["Rabatt"] != DBNull.Value ? Convert.ToInt32(reader["Rabatt"]) : 0;
                            sumRendUnit = reader["Unit"] != DBNull.Value ? Convert.ToInt32(reader["Unit"]) : 0;
                            sumForgalom = reader["Forgalom"] != DBNull.Value ? Convert.ToDouble(reader["Forgalom"]) : 0;
                        } else
                        {
                            log.LogWarning("No records found for the given day range {QueryDate} - {Tomorrow}", 
                                queryDate, tomorrow);
                        }
                    }
                }

                query = @"
                        SELECT Termek, SUM(Rabatt) Rabatt, SUM(Rend_Unit) Unit, SUM(Forgalom) Forgalom
                        FROM [vir].[dbo].[v_rendeles_teteles_u2]
                        WHERE Rogzitve >= @StartDay AND Rogzitve < @EndDay
                        GROUP BY Termek order by Termek;";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.Add("@StartDay", SqlDbType.Date).Value = queryDate;
                    command.Parameters.Add("@EndDay", SqlDbType.Date).Value = tomorrow;
                    command.CommandTimeout = 500;
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Load(reader);
                        GenerateHtmlEmail(sumRabatt, sumRendUnit, sumForgalom, dataTable, config);
                    }
                }
            }
        }

        private void GenerateHtmlEmail(int sumRabatt, int sumRendUnit, double sumForgalom, DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.LogInformation("No records found.");
                return;
            }

            log.LogDebug($"Found {dataTable.Rows.Count} records.");

            StringBuilder htmlBuilder = new StringBuilder();
            AddHeader(sumRabatt, sumRendUnit, sumForgalom, htmlBuilder);
            int orderCounter = 0;
            double overall_Turnover = 0;

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;
                overall_Turnover += Convert.ToDouble(row["Forgalom"]);
                int index = dataTable.Rows.IndexOf(row);
                log.LogDebug($"{index.ToString()}/{dataTable.Rows.Count}");
                AddRow(htmlBuilder, row);
            }

            AddFooter(htmlBuilder);

            string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
            string subject = $"Napi Scriptor értékesítés jelentés";

            Util.SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", overall_Turnover), Environment.GetEnvironmentVariable("VIR_PATIKAMAN_USERNAME"));

            metricService.DailyOrderSum = overall_Turnover;
            metricService.DailyOrderCount = orderCounter;

            log.LogDebug($"Reporting metrics, orderSum: {overall_Turnover}, orderCount: {orderCounter}.");
        }

        private void AddFooter(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("</tr>");
            htmlBuilder.Append("</table>");
            htmlBuilder.Append("<br>");
            htmlBuilder.Append("<p style='font-family: Arial, sans-serif; font-size: 10px; color: #333;'>");
            htmlBuilder.Append("Ezt az üzenetet a VIR Rendelés Értesítő alkalmazás generálta, kérjük ne válaszolj rá.<br>");
            htmlBuilder.Append("Hibabejelentés, észrevétel, javaslat: <a href='mailto:horvath.zsolt@goodwillpharma.com'>horvath.zsolt@goodwillpharma.com</a><br>");
            htmlBuilder.Append("</p>");
            htmlBuilder.Append("</html>");
        }

        private void AddHeader(int sumRabatt, int sumRendUnit, double sumForgalom, StringBuilder htmlBuilder)
        {

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

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append("<th colspan='4' style='text-align:center'>Scriptor rendelések</th>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<th>Dátum</th>");
            htmlBuilder.Append($"<th>Unit</td>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<td>{DateTime.Today.ToShortDateString()}</td>");
            htmlBuilder.Append($"<td>{sumRendUnit} db</td>");
            htmlBuilder.Append($"<td>{sumRabatt} db</td>");
            var _sumForgalom = Math.Round(sumForgalom).ToString("N0", new CultureInfo("hu-HU"));
            htmlBuilder.Append($"<td>{_sumForgalom} Ft</td>");
            htmlBuilder.Append("</tr>");
            htmlBuilder.Append("</table>");

            htmlBuilder.Append("</br>");
            htmlBuilder.Append("</br>");

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append("<th colspan='4' style='text-align:center'>Scriptor termék rendelések</th>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");

            htmlBuilder.Append($"<th>Termék</th>");
            htmlBuilder.Append($"<th>Unit</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
        }

        private void AddRow(StringBuilder htmlBuilder, DataRow row)
        {
            string strTurnOver = Math.Round(row.Field<double>("Forgalom")).ToString("N0", new CultureInfo("hu-HU"));

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<td>{row.Field<string>("Termek")}</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>{row.Field<int>("Unit")} db</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>{row.Field<int>("Rabatt")} db</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>{strTurnOver} Ft</td>");
            htmlBuilder.Append("</tr>");
        }
    }
}

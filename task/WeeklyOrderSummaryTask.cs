using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OrderEmail.service;
using OrderEmail.util;
using System;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OrderEmail.task
{
    [WeeklyOrderSummaryTask]
    public class WeeklyOrderSummaryTask(MetricService metricService, ILogger<WeeklyOrderSummaryTask> log) : ServiceTask
    {
        public void ExecuteTask()
        {
            log.LogInformation("Weekly email sender scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.WeeklyOrderSummaryJobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.WeeklyOrderSummaryJobExecutionStatus = 1;
            metricService.RecordWeeklyOrderSummaryJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
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
                        }
                        else
                        {
                            metricService.WeeklyOrderSummaryJobExecutionStatus = 0;
                            log.LogError("No records found in the DailyOrderMailConfig table.");
                            return;
                        }
                    }
                }

                DateTime today = DateTime.Today;

                // Find Monday of the current week
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                DateTime monday = today.AddDays(-diff);

                // Date-only range
                DateTime weekStart = monday;
                DateTime weekEnd = monday.AddDays(4);

                query = @"
                    SELECT 
                        PARTNER_NEV,
                        PARTNER_HELYSEG,
                        SUM(ARBEVETEL_NFT) AS ARBEVETEL_NFT
                    FROM [dbo].[v_qad_arbevetel_2026]
                    WHERE
                        BELSO_PARTNER = 'N'
                        AND ERT_TIPUS = 'Termék'
                        AND BIZONYLAT_DATUM >= @WeekStart
                        AND BIZONYLAT_DATUM <= @WeekEnd
                    GROUP BY
                        PARTNER_NEV,
                        PARTNER_HELYSEG
                    ORDER BY
                        SUM(ARBEVETEL_NFT) DESC;";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.Add("@WeekStart", SqlDbType.Date).Value = weekStart;
                    command.Parameters.Add("@WeekEnd", SqlDbType.Date).Value = weekEnd;

                    command.CommandTimeout = 0;

                    log.LogInformation(
                        @"Executing SQL:
                        {Query}

                        Parameters:
                        @WeekStart = {WeekStart:yyyy-MM-dd}
                        @WeekEnd   = {WeekEnd:yyyy-MM-dd}",
                        query,
                        weekStart,
                        weekEnd);

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Load(reader);
                        GenerateHtmlEmail(dataTable, config, weekStart, weekEnd);
                    }
                }
            }
        }

        private void GenerateHtmlEmail(DataTable dataTable, Configuration config, DateTime weekStart, DateTime weekEnd)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.LogInformation("No records found.");
                return;
            }

            log.LogDebug($"Found {dataTable.Rows.Count} records.");

            StringBuilder htmlBuilder = new StringBuilder();
            
            int orderCounter = 0;

            double overall_Turnover = dataTable.AsEnumerable()
                .Sum(row => Convert.ToDouble(row["ARBEVETEL_NFT"]));

            string weekStartDate = weekStart.ToString("yyyy-MM-dd");
            string weekEndDate = weekEnd.ToString("yyyy-MM-dd");

            AddHeader(htmlBuilder, weekStartDate, weekEndDate, overall_Turnover);

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;
                int index = dataTable.Rows.IndexOf(row);
                AddRow(htmlBuilder, row);
            }

            AddSummary(overall_Turnover, htmlBuilder);
            AddFooter(htmlBuilder);

            string subject = $"Hétvégi árbevétel értesítő ({weekStartDate} – {weekEndDate})";

            string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);

            Util.SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", overall_Turnover), "horzsolt2006@gmail.com");

            metricService.WeeklyOrderSum = overall_Turnover;
            metricService.WeeklyOrderCount = orderCounter;
            metricService.MarkWeeklyOrderSummaryJobSuccess();

            log.LogDebug($"Reporting metrics, orderSum: {overall_Turnover}, orderCount: {orderCounter}.");
        }

        private static void AddSummary(double sum_Turnover, StringBuilder htmlBuilder)
        {
            string strTurnOver = Math.Round(sum_Turnover).ToString("N0", new CultureInfo("hu-HU"));

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<td align='center'><b>Σ</b></td>");
            htmlBuilder.Append($"<td></td>");
            htmlBuilder.Append($"<td align='right'><b>{strTurnOver} Ft</b></td>");
            htmlBuilder.Append("</tr>");
            htmlBuilder.Append("</table>");
        }

        private void AddFooter(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("<br>");
            htmlBuilder.Append("<p style='font-family: Arial, sans-serif; font-size: 10px; color: #333;'>");
            htmlBuilder.Append("Ezt az üzenetet a VIR Rendelés Értesítő alkalmazás generálta, kérjük ne válaszolj rá.<br>");
            htmlBuilder.Append("Hibabejelentés, észrevétel, javaslat: <a href='mailto:horvath.zsolt@goodwillpharma.com'>horvath.zsolt@goodwillpharma.com</a><br>");
            htmlBuilder.Append("</p>");
            htmlBuilder.Append("</html>");
        }

        private void AddHeader(StringBuilder htmlBuilder, string weekStartDate, string weekEndDate, double sum_Turnover)
        {

            string strTurnOver = Math.Round(sum_Turnover).ToString("N0", new CultureInfo("hu-HU"));

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

            htmlBuilder.Append("<tr class='lowertabletr' height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'><b>Tól</b></td>");
            htmlBuilder.Append($"<td class='simpletd'><b>Ig</b></td>");
            htmlBuilder.Append($"<td align='center'><b>Σ</b></td>");
            htmlBuilder.Append("</tr>");
            htmlBuilder.Append("<tr class='lowertabletr' height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'><b>{weekStartDate}</b></td>");
            htmlBuilder.Append($"<td class='simpletd'><b>{weekEndDate}</b></td>");
            htmlBuilder.Append($"<td align='right'><b>{strTurnOver} Ft</b></td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append("<td></td>");
            htmlBuilder.Append("<td></td>");
            htmlBuilder.Append("<td></td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'><b>Név</b></td>");
            htmlBuilder.Append($"<td class='simpletd'><b>Helység</b></td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'><b>Árbevétel</b></td>");
            htmlBuilder.Append("</tr>");
        }

        private void AddRow(StringBuilder htmlBuilder, DataRow row)
        {
            string strTurnOver = Math.Round(row.Field<decimal>("ARBEVETEL_NFT")).ToString("N0", new CultureInfo("hu-HU"));

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td>{row.Field<string>("PARTNER_NEV")}</td>");
            htmlBuilder.Append($"<td class='simpletd'>{row.Field<string>("PARTNER_HELYSEG")}</td>");
            htmlBuilder.Append($"<td  align='right' class='simpletd'>{strTurnOver} Ft</td>");
            htmlBuilder.Append("</tr>");
        }
    }
}

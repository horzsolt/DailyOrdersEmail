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
    public class WeeklyProductOrderSummaryTask(MetricService metricService, ILogger<WeeklyProductOrderSummaryTask> log) : ServiceTask
    {
        private static readonly CultureInfo HuCulture = new CultureInfo("hu-HU");

        public void ExecuteTask()
        {
            log.LogInformation("Weekly product turnover email scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.WeeklyProductOrderSummaryJobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.WeeklyProductOrderSummaryJobExecutionStatus = 1;
            metricService.RecordWeeklyProductOrderSummaryJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
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

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();

                Configuration config;
                try
                {
                    config = Util.LoadConfiguration(connection);
                    log.LogDebug($"Test mode: {config.TestMode}");
                }
                catch (Exception ex)
                {
                    metricService.WeeklyProductOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to load configuration (weekly product).");
                    return;
                }

                DateTime today = DateTime.Today;
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                DateTime monday = today.AddDays(-diff);

                DateTime weekStart = monday;
                DateTime weekEnd = monday.AddDays(4);

                string query = @"
                SELECT
                    TERMEK_NEV,
                    SUM(MENNYISEG) AS MENNYISEG,
                    SUM(ARBEVETEL_NFT) AS ARBEVETEL_NFT
                FROM [dbo].[v_qad_arbevetel_union] WITH (NOLOCK)
                WHERE
                    BELSO_PARTNER = 'N'
                    AND ERT_TIPUS = 'Termék'
                    AND BIZONYLAT_DATUM >= @WeekStart
                    AND BIZONYLAT_DATUM <= @WeekEnd
                GROUP BY
                    TERMEK_NEV
                ORDER BY
                    SUM(ARBEVETEL_NFT) DESC;";

                DataTable data;
                try
                {
                    data = Util.GetDataWithRetry(
                        connection,
                        query,
                        cmd =>
                        {
                            cmd.Parameters.Add("@WeekStart", SqlDbType.Date).Value = weekStart;
                            cmd.Parameters.Add("@WeekEnd", SqlDbType.Date).Value = weekEnd;

                            log.LogInformation(
                                @"Parameters:
                          @WeekStart = {WeekStart:yyyy-MM-dd}",
                                weekStart);
                            log.LogInformation(
                                @"Parameters:
                          @WeekEnd = {WeekEnd:yyyy-MM-dd}",
                                weekEnd);
                        });
                }
                catch (Exception ex)
                {
                    metricService.WeeklyProductOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to retrieve weekly product data after retries.");
                    return;
                }

                GenerateHtmlEmail(data, config, weekStart, weekEnd);
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
            double overallQuantity = dataTable.AsEnumerable()
                .Sum(row => Convert.ToDouble(row["MENNYISEG"]));
            double overallTurnover = dataTable.AsEnumerable()
                .Sum(row => Convert.ToDouble(row["ARBEVETEL_NFT"]));

            string weekStartDate = weekStart.ToString("yyyy-MM-dd");
            string weekEndDate = weekEnd.ToString("yyyy-MM-dd");

            AddHeader(htmlBuilder, weekStartDate, weekEndDate, overallTurnover);

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;
                AddRow(htmlBuilder, row);
            }

            AddSummary(overallQuantity, overallTurnover, htmlBuilder);
            AddFooter(htmlBuilder);

            string subject = $"Hétvégi Termék árbevétel QAD ({weekStartDate} – {weekEndDate})";

            Util.SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", overallTurnover));

            metricService.WeeklyProductOrderSum = overallTurnover;
            metricService.WeeklyProductOrderCount = orderCounter;
            metricService.MarkWeeklyProductOrderSummaryJobSuccess();

            log.LogDebug(
                $"Reporting metrics, productSum: {overallTurnover}, productQuantity: {overallQuantity}, productCount: {orderCounter}.");
        }

        private static void AddSummary(double sumQuantity, double sumTurnover, StringBuilder htmlBuilder)
        {
            string strQuantity = Math.Round(sumQuantity).ToString("N0", HuCulture);
            string strTurnover = Math.Round(sumTurnover).ToString("N0", HuCulture);

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<td align='center'><b>Σ</b></td>");
            htmlBuilder.Append($"<td align='right'><b>{strQuantity}</b></td>");
            htmlBuilder.Append($"<td align='right'><b>{strTurnover} Ft</b></td>");
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

        private void AddHeader(StringBuilder htmlBuilder, string weekStartDate, string weekEndDate, double sumTurnover)
        {
            string strTurnOver = Math.Round(sumTurnover).ToString("N0", HuCulture);

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
            htmlBuilder.Append($"<td class='simpletd'><b>Termék</b></td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'><b>Mennyiség</b></td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'><b>Árbevétel</b></td>");
            htmlBuilder.Append("</tr>");
        }

        private void AddRow(StringBuilder htmlBuilder, DataRow row)
        {
            string strQuantity = Math.Round(row.Field<decimal>("MENNYISEG")).ToString("N0", HuCulture);
            string strTurnover = Math.Round(row.Field<decimal>("ARBEVETEL_NFT")).ToString("N0", HuCulture);

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td>{row.Field<string>("TERMEK_NEV")}</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>{strQuantity}</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>{strTurnover} Ft</td>");
            htmlBuilder.Append("</tr>");
        }
    }
}

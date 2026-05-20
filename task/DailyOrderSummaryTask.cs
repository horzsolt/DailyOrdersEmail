using OrderEmail.util;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using OrderEmail.service;
using System;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OrderEmail.task
{
    [DailyOrderSummaryTask]
    public class DailyOrderSummaryTask(MetricService metricService, ILogger<DailyOrderSummaryTask> log) : ServiceTask
    {
        public void ExecuteTask()
        {
            log.LogInformation("5pm email sender scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.DailyOrderSummaryJobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.DailyOrderSummaryJobExecutionStatus = 1;
            metricService.RecordDailyOrderSummaryJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
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
                    metricService.DailyOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to load configuration (daily).");
                    return;
                }

                DateTime queryDate = DateTime.Today;
                // queryDate = new DateTime(2025, 10, 17);

                string query = @"
            SELECT 
                PARTNER_NEV,
                PARTNER_HELYSEG,
                SUM(ARBEVETEL_NFT) AS ARBEVETEL_NFT
            FROM [dbo].[v_qad_arbevetel_union] WITH (NOLOCK)
            WHERE
                BELSO_PARTNER = 'N'
                AND ERT_TIPUS = 'Termék'
                AND BIZONYLAT_DATUM = @BizDatum
            GROUP BY
                PARTNER_NEV,
                PARTNER_HELYSEG
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
                            cmd.Parameters.Add("@BizDatum", SqlDbType.Date).Value = queryDate;

                            log.LogInformation(
                                @"Parameters:
                          @BizDatum = {BizDatum:yyyy-MM-dd}",
                                queryDate);
                        });
                }
                catch (Exception ex)
                {
                    metricService.DailyOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to retrieve daily data after retries.");
                    return;
                }

                GenerateHtmlEmail(data, config);
            }
        }

        private void GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.LogInformation("No records found.");
                return;
            }

            log.LogDebug($"Found {dataTable.Rows.Count} records.");

            StringBuilder htmlBuilder = new StringBuilder();
            AddHeader(htmlBuilder);
            int orderCounter = 0;
            double overall_Turnover = 0;

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;
                overall_Turnover += Convert.ToDouble(row["ARBEVETEL_NFT"]);
                int index = dataTable.Rows.IndexOf(row);
                log.LogDebug($"{index.ToString()}/{dataTable.Rows.Count}");
                AddRow(htmlBuilder, row);
            }

            AddSummary(overall_Turnover, htmlBuilder);
            AddFooter(htmlBuilder);

            string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
            string subject = $"Napvégi árbevétel értesítő";

            Util.SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", overall_Turnover));

            metricService.DailyOrderSum = overall_Turnover;
            metricService.DailyOrderCount = orderCounter;

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

        private void AddHeader(StringBuilder htmlBuilder)
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

            htmlBuilder.Append($"<table class='uppertable'>");

            htmlBuilder.Append("<tr height='20px'>");
            htmlBuilder.Append($"<td class='simpletd'>Név</td>");
            htmlBuilder.Append($"<td class='simpletd'>Helység</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>Árbevétel</td>");
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

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
    public class DailyProductOrderSummaryTask(MetricService metricService, ILogger<DailyProductOrderSummaryTask> log) : ServiceTask
    {
        private static readonly CultureInfo HuCulture = new CultureInfo("hu-HU");

        public void ExecuteTask()
        {
            log.LogInformation("Daily product turnover email scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                CheckForNewRecords();
            }
            catch (Exception ex)
            {
                metricService.DailyProductOrderSummaryJobExecutionStatus = 0;
                log.LogError($"Error: {ex}");
            }

            log.LogInformation("Scheduled run finished.");
            stopwatch.Stop();
            metricService.DailyProductOrderSummaryJobExecutionStatus = 1;
            metricService.RecordDailyProductOrderSummaryJobExecutionDuration(Convert.ToInt32(stopwatch.Elapsed.TotalSeconds));
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
                    metricService.DailyProductOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to load configuration (daily product).");
                    return;
                }

                DateTime queryDate = DateTime.Today;

                string query = @"
            SELECT
                TERMEK_NEV,
                SUM(MENNYISEG) AS MENNYISEG,
                SUM(ARBEVETEL_NFT) AS ARBEVETEL_NFT
            FROM [dbo].[v_qad_arbevetel_union] WITH (NOLOCK)
            WHERE
                BELSO_PARTNER = 'N'
                AND ERT_TIPUS = 'Termék'
                AND BIZONYLAT_DATUM = @BizDatum
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
                            cmd.Parameters.Add("@BizDatum", SqlDbType.Date).Value = queryDate;

                            log.LogInformation(
                                @"Parameters:
                          @BizDatum = {BizDatum:yyyy-MM-dd}",
                                queryDate);
                        });
                }
                catch (Exception ex)
                {
                    metricService.DailyProductOrderSummaryJobExecutionStatus = 0;
                    log.LogError(ex, "Failed to retrieve daily product data after retries.");
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
            double overallQuantity = 0;
            double overallTurnover = 0;

            foreach (DataRow row in dataTable.Rows)
            {
                orderCounter++;
                overallQuantity += Convert.ToDouble(row["MENNYISEG"]);
                overallTurnover += Convert.ToDouble(row["ARBEVETEL_NFT"]);
                AddRow(htmlBuilder, row);
            }

            AddSummary(overallQuantity, overallTurnover, htmlBuilder);
            AddFooter(htmlBuilder);

            string subject = "Napvégi Termék árbevétel QAD";

            Util.SendEmail(htmlBuilder.ToString(), config, subject, string.Format("{0:C0}", overallTurnover));

            metricService.DailyProductOrderSum = overallTurnover;
            metricService.DailyProductOrderCount = orderCounter;

            log.LogDebug($"Reporting metrics, productSum: {overallTurnover}, productQuantity: {overallQuantity}, productCount: {orderCounter}.");
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
            htmlBuilder.Append($"<td class='simpletd'>Termék</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>Mennyiség</td>");
            htmlBuilder.Append($"<td align='right' class='simpletd'>Árbevétel</td>");
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

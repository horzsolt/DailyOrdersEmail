using System;

namespace DailyOrdersEmail.task
{
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using DailyOrdersEmail.util;
    using Microsoft.Extensions.Logging;
    using Microsoft.Data.SqlClient;
   
    class CsvToHtmlTableConverter(ILogger<PatikamanTask> log)
    {

        string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                          $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                          $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                          $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};" +
                          "Connection Timeout=500;Trust Server Certificate=true";
        class CsvRow
        {
            public string Termeknev { get; set; }
            public string Rogzito { get; set; }
            public string Gyogyszertar { get; set; }
            public int Kedvezmeny { get; set; }
            public int Mennyiseg { get; set; }
            public int CsomagDB { get; set; }
        }

        private int GetCsomagDB(string termeknev, SqlConnection connection)
        {
            int csomagDB = 0;

            string query = "SELECT TOP 1 CSOMAG_DB FROM dbo.Patikaman WHERE CSOMAG_KEDV = @Termeknev";
            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@Termeknev", termeknev);
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        csomagDB = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    }
                }

            }

            if (csomagDB == 0)
            {
                log.LogError($"No csomagDB found for termeknev: {termeknev}");
            }
            else
            {
                log.LogDebug($"CsomagDB for {termeknev}: {csomagDB}");
            }
            return csomagDB;
        }

        private Configuration GetEmailConfiguration()
        {

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

                            return config;
                        }
                        else
                        {
                            log.LogError("No records found in the DailyOrderMailConfig table.");
                            return null;
                        }
                    }
                }
            }
        }

        public void ParseCSV(string csvPath, DateTime targetDate)
        {
            var rows = new List<CsvRow>();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(1250);

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var reader = new StreamReader(csvPath, encoding))
                {
                    string header = reader.ReadLine(); // Skip first row
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var parts = line.Split(';');

                        if (parts.Length < 35)
                            continue;

                        if (!DateTime.TryParse(parts[5].Trim('"'), out var rowDate))
                            continue;

                        if (rowDate.Date != targetDate.Date)
                            continue;

                        rows.Add(new CsvRow
                        {
                            Termeknev = parts[32].Trim('"'),
                            Rogzito = parts[4].Trim('"'),
                            Gyogyszertar = parts[3].Trim('"'),
                            Kedvezmeny = ParseKedvezmeny(parts[35]),
                            Mennyiseg = int.TryParse(parts[33], out var qty) ? qty : 0,
                            CsomagDB = GetCsomagDB(parts[2].Trim('"') + "_" + ParseKedvezmeny(parts[35]), connection)
                        });
                    }
                }
            }

            SendHtmlTableForProduct(rows, targetDate);
            SendHtmlTableGroupedByRogzitoAndGyogyszertar(rows, targetDate);
        }

        private void SendHtmlTableForProduct(List<CsvRow> rows, DateTime targetDate)
        {
            int totalMennyiseg;
            StringBuilder sb = new StringBuilder();

            var grouped = rows
                .GroupBy(r => r.Termeknev)
                .Select(g =>
                {
                    var kedvezmenyList = g
                        .GroupBy(r => r.Kedvezmeny)
                        .Select(sg =>
                        {
                            int totalMennyiseg = sg.Sum(r => r.Mennyiseg);
                            int csomagDb = sg.First().CsomagDB;

                            double darabPerCsomag = csomagDb != 0
                                ? (double)totalMennyiseg / csomagDb
                                : 0;

                            return new
                            {
                                Kedvezmeny = sg.Key,
                                Mennyiseg = totalMennyiseg,
                                CsomagDB = csomagDb,
                                DarabPerCsomag = darabPerCsomag
                            };
                        })
                        .OrderByDescending(x => x.Mennyiseg)
                        .ToList();

                    return new
                    {
                        Termeknev = g.Key,
                        Kedvezmenyek = kedvezmenyList,
                        TotalMennyiseg = kedvezmenyList.Sum(x => x.Mennyiseg)
                    };
                })
                .OrderByDescending(x => x.TotalMennyiseg)
                .ToDictionary(
                    x => x.Termeknev,
                    x => x.Kedvezmenyek
                );


            totalMennyiseg = rows.Sum(r => r.Mennyiseg);

            AddHeaderTermek(sb);

            foreach (var kvp in grouped)
            {
                sb.AppendLine("<tr class='lowertabletr'>");
                sb.Append($"<td class='lowertablecell' align='right'>{kvp.Key}</td>");

                sb.Append("<td class='lowertablecell' align='right'>");
                foreach (var pair in kvp.Value)
                {
                    sb.Append($"{pair.Kedvezmeny} %<br>");
                }
                sb.Append("</td>");

                sb.Append("<td class='lowertablecell' align='right'>");
                foreach (var pair in kvp.Value)
                {
                    sb.Append($"{pair.Mennyiseg}<br>");
                }
                sb.Append("</td>");
                sb.Append("<td class='lowertablecell' align='right'>");
                foreach (var pair in kvp.Value)
                {
                    sb.Append($"{pair.CsomagDB}<br>");
                }
                sb.Append("</td>");

                sb.Append("<td class='lowertablecell' align='right'>");
                foreach (var pair in kvp.Value)
                {
                    sb.Append($"{pair.DarabPerCsomag}<br>");
                }
                sb.Append("</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine($"<tr class='lowertabletr'><td align='right'><strong>Összesen</strong></td><td></td><td align='right'><strong>{totalMennyiseg}</strong></td><td></td></tr>");
            sb.AppendLine("</table>");
            AddFooter(sb);

            string subject = $"Napi Patikamanagement értesítő (termék) {targetDate.ToString("yyyy. MM. dd.")}";
            Util.SendEmail(sb.ToString(), GetEmailConfiguration(), subject, totalMennyiseg + " db");
        }

        private void SendHtmlTableGroupedByRogzitoAndGyogyszertar(List<CsvRow> rows, DateTime targetDate)
        {
            int totalMennyiseg = rows.Sum(r => r.Mennyiseg);
            StringBuilder sb = new StringBuilder();

            var grouped = rows
                .GroupBy(r => r.Rogzito)
                .ToDictionary(
                    rg => rg.Key,
                    rg => rg
                        .GroupBy(r => r.Gyogyszertar)
                        .ToDictionary(
                            gg => gg.Key,
                            gg => gg
                                .GroupBy(r => r.Termeknev)
                                .ToDictionary(
                                    tg => tg.Key,
                                    tg =>
                                    {
                                        var kedvezmenyList = tg
                                            .GroupBy(r => r.Kedvezmeny)
                                            .Select(sg =>
                                            {
                                                int total = sg.Sum(r => r.Mennyiseg);
                                                int csomagDb = sg.First().CsomagDB;
                                                double darabPerCsomag = csomagDb != 0 ? (double)total / csomagDb : 0;

                                                return new
                                                {
                                                    Kedvezmeny = sg.Key,
                                                    Mennyiseg = total,
                                                    CsomagDB = csomagDb,
                                                    DarabPerCsomag = darabPerCsomag
                                                };
                                            })
                                            .ToList();

                                        return kedvezmenyList;
                                    }
                                )
                        )
                );


            AddHeaderErtekesito(sb);

            foreach (var rogzito in grouped)
            {
                foreach (var gyogyszertar in rogzito.Value)
                {
                    bool isFirst = true;
                    foreach (var termek in gyogyszertar.Value
                        .OrderByDescending(kvp => kvp.Value.Sum(x => x.Mennyiseg)))
                    {
                        if (isFirst)
                        {
                            sb.AppendLine("<tr class='lowertabletr'>");
                            sb.Append($"<td class='lowertablecell' align='right'>{rogzito.Key}</td>");
                            sb.Append($"<td class='lowertablecell' align='right'>{gyogyszertar.Key}</td>");
                            isFirst = false;
                        } else
                        {
                            sb.Append($"<td class='lowertablecell' align='right'></td>");
                            sb.Append($"<td class='lowertablecell' align='right'></td>");
                        }

                        sb.Append($"<td class='lowertablecell' align='right'>{termek.Key}</td>");

                        sb.Append("<td class='lowertablecell' align='right'>");
                        foreach (var pair in termek.Value)
                        {
                            sb.Append($"{pair.Kedvezmeny} %<br>");
                        }
                        sb.Append("</td>");

                        sb.Append("<td class='lowertablecell' align='right'>");
                        foreach (var pair in termek.Value)
                        {
                            sb.Append($"{pair.Mennyiseg}<br>");
                        }
                        sb.Append("</td>");

                        sb.Append("<td class='lowertablecell' align='right'>");
                        foreach (var pair in termek.Value)
                        {
                            sb.Append($"{pair.CsomagDB}<br>");
                        }
                        sb.Append("</td>");

                        sb.Append("<td class='lowertablecell' align='right'>");
                        foreach (var pair in termek.Value)
                        {
                            sb.Append($"{pair.DarabPerCsomag}<br>");
                        }
                        sb.Append("</td>");
                        sb.AppendLine("</tr>");
                    }
                }
                
            }

            sb.AppendLine($"<tr class='lowertabletr'><td align='right' colspan='3'><strong>Összesen</strong></td><td></td><td align='right'><strong>{totalMennyiseg}</strong></td><td></td><td></td></tr>");
            sb.AppendLine("</table>");
            AddFooter(sb);

            string subject = $"Napi Patikamanagement értesítő (REP) {targetDate:yyyy. MM. dd.}";
            Util.SendEmail(sb.ToString(), GetEmailConfiguration(), subject, $"{totalMennyiseg} db");
        }


        private int ParseKedvezmeny(string raw)
        {
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.GetCultureInfo("hu-HU"), out var value))
            {
                return ((int)value);
            }
            return 0;
        }

        private void AddHeaderErtekesito(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");

            htmlBuilder.Append("<head>");
            htmlBuilder.Append("<style>");
            htmlBuilder.Append(".uppertable {border: none;} ");
            htmlBuilder.Append("table {border-collapse: collapse; } ");
            htmlBuilder.Append("th { background: #6BAFBC; font-size:8.5pt;font-family:'Arial',sans-serif; padding: 1pt 1pt 1pt 1pt;} ");
            htmlBuilder.Append("td { border: none; background: #FFFCF2; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; } ");
            htmlBuilder.Append("td.lowertablecell { border-bottom: 5px solid white; background: #FFFCF2; font-size: 8.5pt; font-family: 'Arial', sans-serif; padding: .75pt .75pt .75pt .75pt;}");
            htmlBuilder.Append(".lowertabletr { border-bottom: 1px solid black; height: 30px;padding: 1pt 1pt 1pt 1pt; } ");
            htmlBuilder.Append(".simpletd { border: none; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; background: #FFFCF2; color:#333333;} ");
            htmlBuilder.Append("tf { background: #6BAFBC; padding: .75pt .75pt .75pt .75pt; font-size:8.5pt;font-family:'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in;margin-bottom:15.0pt;margin-left:0in; } ");
            htmlBuilder.Append("</style>");
            htmlBuilder.Append("</head>");

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<th>Rögzítő felhasználó</th>");
            htmlBuilder.Append($"<th>Gyógyszertár</th>");
            htmlBuilder.Append($"<th>Terméknév</th>");
            htmlBuilder.Append($"<th>Kedvezmény</th>");
            htmlBuilder.Append($"<th>Mennyiség</th>");
            htmlBuilder.Append($"<th>Db/Csomag</th>");
            htmlBuilder.Append($"<th>Csomag</th>");
            htmlBuilder.Append("</tr>");
        }

        private void AddHeaderTermek(StringBuilder htmlBuilder)
        {
            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");

            htmlBuilder.Append("<head>");
            htmlBuilder.Append("<style>");
            htmlBuilder.Append(".uppertable {border: none;} ");
            htmlBuilder.Append("table {border-collapse: collapse; } ");
            htmlBuilder.Append("th { background: #6BAFBC; font-size:8.5pt;font-family:'Arial',sans-serif; padding: 1pt 1pt 1pt 1pt;} ");
            htmlBuilder.Append("td { border: none; background: #FFFCF2; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; } ");
            htmlBuilder.Append("td.lowertablecell { border-bottom: 5px solid white; background: #FFFCF2; font-size: 8.5pt; font-family: 'Arial', sans-serif; padding: .75pt .75pt .75pt .75pt;}");
            htmlBuilder.Append(".lowertabletr { border-bottom: 1px solid black; height: 30px;padding: 1pt 1pt 1pt 1pt; } ");
            htmlBuilder.Append(".simpletd { border: none; font-size:8.5pt;font-family:'Arial',sans-serif; padding: .75pt .75pt .75pt .75pt; background: #FFFCF2; color:#333333;} ");
            htmlBuilder.Append("tf { background: #6BAFBC; padding: .75pt .75pt .75pt .75pt; font-size:8.5pt;font-family:'Arial',sans-serif;color:#333333; margin-top:7.5pt;margin-right:0in;margin-bottom:15.0pt;margin-left:0in; } ");
            htmlBuilder.Append("</style>");
            htmlBuilder.Append("</head>");

            htmlBuilder.Append($"<table cellspacing='0' cellpadding='0'>");

            htmlBuilder.Append($"<tr class='lowertabletr'>");
            htmlBuilder.Append($"<th>Terméknév</th>");
            htmlBuilder.Append($"<th>Kedvezmény</th>");
            htmlBuilder.Append($"<th>Mennyiség</th>");
            htmlBuilder.Append($"<th>Db/Csomag</th>");
            htmlBuilder.Append($"<th>Csomag</th>");
            htmlBuilder.Append("</tr>");
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
    }
}

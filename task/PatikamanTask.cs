using DailyOrdersEmail.services;
using Microsoft.Extensions.Logging;
using System.IO;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Globalization;

namespace DailyOrdersEmail.task
{

    [PatikamanTask]
    public class PatikamanTask(MetricService metricService, ILogger<PatikamanTask> log) : ServiceTask
    {
        string csvFileName = "patikaman.csv";

        private void DownloadCsv_Http()
        {
            string username = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_USERNAME");
            string password = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_PWD");

            string csvUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_CSVURL");
            string loginUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_LOGINURL");

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AllowAutoRedirect = true
            };

            using (var client = new HttpClient(handler))
            {
                client.BaseAddress = new Uri("https://dashboard.patikamanagement.hu");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:138.0) Gecko/20100101 Firefox/13");
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br, zstd");
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("hu-HU,hu;q=0.8,en-US;q=0.5,en;q=0.3");
                client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");
                client.DefaultRequestHeaders.Host = "dashboard.patikamanagement.hu";
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Referer", "https://dashboard.patikamanagement.hu/");
                client.DefaultRequestHeaders.Add("Origin", "https://dashboard.patikamanagement.hu");
                client.DefaultRequestHeaders.Add("DNT", "1");
                client.DefaultRequestHeaders.Add("Priority", "u=0, i");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                var _response = client.GetAsync("/").Result;

                var _cookies = handler.CookieContainer.GetCookies(new Uri("https://dashboard.patikamanagement.hu"));
                foreach (System.Net.Cookie cookie in _cookies)
                {
                    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                }

                var loginData = new Dictionary<string, string>
                {
                    { "email", username },
                    { "password", password },
                    { "redirect_url", "" },
                    { "response", "json" }
                };

                var loginTask = client.PostAsync("/login/login", new FormUrlEncodedContent(loginData));
                loginTask.Wait();
                var loginResponse = loginTask.Result;

                if (!loginResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("❌ Login failed. Status code: " + loginResponse.StatusCode);
                    return;
                }

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

                var response = client.GetAsync(csvUrl + "/export").Result;

                if (!response.IsSuccessStatusCode)
                {
                    log.LogDebug("❌ Download failed with status: " + response.StatusCode);
                    return;
                }

                using (var stream = response.Content.ReadAsStreamAsync().Result)
                using (var fileStream = File.Create("downloaded3.csv"))
                {
                    stream.CopyTo(fileStream);
                }

                log.LogDebug("✅ CSV file downloaded and saved as downloaded3.csv");
                CsvToHtmlTableConverter converter = new CsvToHtmlTableConverter(log);
                converter.ParseCSV("downloaded3.csv", DateTime.Today);
                //converter.ParseCSV("downloaded3.csv", DateTime.ParseExact("2025.05.16", "yyyy.MM.dd", CultureInfo.InvariantCulture));
            }
        }

        public void ExecuteTask()
        {
            log.LogInformation("Scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                DownloadCsv_Http();
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());
            }

            stopwatch.Stop();
            log.LogInformation($"Elapsed Time: {stopwatch.Elapsed.Hours} hours, {stopwatch.Elapsed.Minutes} minutes, {stopwatch.Elapsed.Seconds} seconds");
        }
    }
}

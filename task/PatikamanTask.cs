using DailyOrdersEmail.services;
using Microsoft.Extensions.Logging;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;

namespace DailyOrdersEmail.task
{

    [PatikamanTask]
    public class PatikamanTask(MetricService metricService, ILogger<PatikamanTask> log) : ServiceTask
    {
        string csvFileName = "patikaman.csv";

        static bool WaitForDownload(string folder, string extension, int timeoutSeconds)
        {
            int waited = 0;
            while (waited < timeoutSeconds)
            {
                var file = Directory.GetFiles(folder).FirstOrDefault(f => f.EndsWith(extension));
                if (file != null)
                {
                    return true;
                }

                Thread.Sleep(1000);
                waited++;
            }

            return false;
        }

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
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:138.0) Gecko/20100101 Firefox/13");
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
                client.DefaultRequestHeaders.Add("Accept-Language", "hu-HU,hu;q=0.8,en-US;q=0.5,en;q=0.3");
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                client.DefaultRequestHeaders.Add("Host", "dashboard.patikamanagement.hu");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Referer", "https://dashboard.patikamanagement.hu/");
                client.DefaultRequestHeaders.Add("Origin", "https://dashboard.patikamanagement.hu");
                client.DefaultRequestHeaders.Add("DNT", "1");
                client.DefaultRequestHeaders.Add("Priority", "u=0, i");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                string[] lines = File.ReadAllLines(@"C:\VIR\patikaman.ini");
                string phpsessid = lines
                    .Where(line => line.StartsWith("PHPSESSID=", StringComparison.OrdinalIgnoreCase))
                    .Select(line => line.Substring("PHPSESSID=".Length).Trim())
                    .FirstOrDefault();

                string url = "https://dashboard.patikamanagement.hu/main/portfolio_max/packages/time_period/10301/data/orders/export";
                string cookieHeader = $"PHPSESSID={phpsessid}";

                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

                client.DefaultRequestHeaders.Add("Cookie", cookieHeader);

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
                converter.GenerateTable_1("downloaded3.csv", DateTime.Today);
            }
        }

        private void DownloadCsv_ChromeDriver()
        {
            var service = ChromeDriverService.CreateDefaultService(@"c:\VIR");
            service.LogPath = @"c:\VIR\dailymail_log\chromedriver.log";
            //service.Port = 59237;
            //service.AllowedIPAddresses
            //service.EnableVerboseLogging = true;

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--window-size=1920,1080");
            options.AddUserProfilePreference("download.default_directory", Directory.GetCurrentDirectory());
            options.AddUserProfilePreference("download.prompt_for_download", false);
            options.AddUserProfilePreference("download.directory_upgrade", true);
            options.AddUserProfilePreference("safebrowsing.enabled", true);

            string username = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_USERNAME");
            string password = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_PWD");

            string csvUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_CSVURL");
            string loginUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_LOGINURL");
            using (IWebDriver driver = new ChromeDriver(service, options))
            {
                log.LogDebug("loginUrl: " + loginUrl);
                driver.Navigate().GoToUrl(loginUrl);
                Thread.Sleep(5000);

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

                var inputFields = wait.Until(d =>
                {
                    var inputs = d.FindElements(By.CssSelector("input"));

                    log.LogDebug("Login inputs:");

                    foreach (var inputField in inputs)
                    {
                        log.LogDebug($"{inputField.Text}");
                    }
                    return inputs.Count >= 2 ? inputs : null;
                });

                IWebElement usernameField = wait.Until(d => d.FindElement(By.CssSelector("input[name='email']")));
                IWebElement passwordField = driver.FindElement(By.CssSelector("input[name='password']"));
                IWebElement loginButton = driver.FindElement(By.CssSelector("button[type='submit']"));

                usernameField.SendKeys(username);
                passwordField.SendKeys(password);
                loginButton.Click();

                Thread.Sleep(5000);

                driver.Navigate().GoToUrl(csvUrl);

                Thread.Sleep(5000);

                var buttons = driver.FindElements(By.XPath("//*[contains(text(), 'Leadott rendelések CSV export')]"));
                if (buttons.Count == 0)
                {
                    log.LogError("Leadott rendelések CSV export button not found.");
                    return;
                }

                buttons[0].Click();
                log.LogDebug("Download triggered...");

                bool downloaded = WaitForDownload(Directory.GetCurrentDirectory(), ".csv", timeoutSeconds: 15);
                if (downloaded)
                {
                    var downloadedFile = Directory.GetFiles(Directory.GetCurrentDirectory())
                                                  .FirstOrDefault(f => f.EndsWith(".csv"));

                    if (downloadedFile != null)
                    {
                        string targetPath = Path.Combine(Directory.GetCurrentDirectory(), "downloaded2.csv");
                        File.Move(downloadedFile, targetPath, overwrite: true);
                        CsvToHtmlTableConverter converter = new CsvToHtmlTableConverter(log);
                        converter.GenerateTable_1(targetPath, DateTime.Today);
                    }
                }
                else
                {
                    log.LogError("CSV file download timed out or failed.");
                }
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

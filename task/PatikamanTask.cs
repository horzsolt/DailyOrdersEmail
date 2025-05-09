using DailyOrdersEmail.services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
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

        private void DownloadCsv()
        {
            //https://dashboard.patikamanagement.hu/main/portfolio_max/packages/time_period/10301/data/orders/export

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

                var _response = client.GetAsync("/").Result;

                var _cookies = handler.CookieContainer.GetCookies(new Uri("https://dashboard.patikamanagement.hu"));
                foreach (System.Net.Cookie cookie in _cookies)
                {
                    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                }

                var loginData = new Dictionary<string, string>
                {
                    { "email", username },
                    { "password", password }
                };

                var loginTask = client.PostAsync("/#login", new FormUrlEncodedContent(loginData));
                loginTask.Wait();
                var loginResponse = loginTask.Result;

                if (!loginResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("❌ Login failed. Status code: " + loginResponse.StatusCode);
                    return;
                }

                var cookies = handler.CookieContainer.GetCookies(new Uri("https://dashboard.patikamanagement.hu"));
                foreach (System.Net.Cookie cookie in cookies)
                {
                    Console.WriteLine($"Cookie: {cookie.Name} = {cookie.Value}");
                }

                Console.WriteLine("✅ Login successful.");

                //string phpsessid = cookies["PHPSESSID"].Value;
                string phpsessid = "0usdf2ubjsk6v0v9pu3mab57j6";

                string url = "https://dashboard.patikamanagement.hu/main/portfolio_max/packages/time_period/10301/data/orders/export";
                string cookieHeader = $"PHPSESSID={phpsessid}";
                string arguments = $"-L -H \"User-Agent: Mozilla/5.0\" -H \"Cookie: {cookieHeader}\" -o downloaded.csv \"{url}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    log.LogDebug("Output:\n" + output);
                    log.LogError("Errors:\n" + error);
                }

                if (!File.Exists("downloaded.csv"))
                {
                    log.LogError("❌ CSV file not found after download.");
                    return;
                }
                CsvToHtmlTableConverter converter = new CsvToHtmlTableConverter(log);
                converter.GenerateTable_1("downloaded.csv", DateTime.Today);

                // STEP 2: Download the CSV file
                //client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Cast<System.Net.Cookie>().Select(c => $"{c.Name}={c.Value}")));
                //handler.CookieContainer.Add(client.BaseAddress, new System.Net.Cookie("PHPSESSID", cookies["PHPSESSID"].Value));

                return;
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:138.0) Gecko/20100101 Firefox/13");
                client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
                client.DefaultRequestHeaders.Add("Accept-Language", "hu-HU,hu;q=0.8,en-US;q=0.5,en;q=0.3");
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                client.DefaultRequestHeaders.Add("Host", "dashboard.patikamanagement.hu");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
                client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
                client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
                client.DefaultRequestHeaders.Add("Referer", "https://dashboard.patikamanagement.hu/");
                client.DefaultRequestHeaders.Add("DNT", "1");
                client.DefaultRequestHeaders.Add("Priority", "u=0, i");
                client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

                var response = client.GetAsync("/main/portfolio_max/packages/time_period/10301/data/orders/export").Result;

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("❌ Download failed with status: " + response.StatusCode);
                    return;
                }

                using (var responseStream = response.Content.ReadAsStreamAsync().Result)
                using (var decompressed = new GZipStream(responseStream, CompressionMode.Decompress))
                using (var fs = new FileStream("downloaded.csv", FileMode.Create, FileAccess.Write))
                {
                    decompressed.CopyTo(fs);
                }

                Console.WriteLine("✅ CSV file downloaded and saved as downloaded.csv");
            }
        }

        private void _DownloadCsv()
        {
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
            using (IWebDriver driver = new ChromeDriver(options))
            {
                driver.Navigate().GoToUrl(loginUrl);

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));

                var inputFields = wait.Until(d =>
                {
                    var inputs = d.FindElements(By.CssSelector("input"));
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

                Thread.Sleep(3000);

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
                        string targetPath = Path.Combine(Directory.GetCurrentDirectory(), "downloaded.csv");
                        File.Move(downloadedFile, targetPath, overwrite: true);
                        CsvToHtmlTableConverter converter = new CsvToHtmlTableConverter(log);
                        converter.GenerateTable_1(targetPath, DateTime.Today);
                    }
                }
                else
                {
                    log.LogError("❌ CSV file download timed out or failed.");
                }
            }
        }

        /*private async Task Run()
        {
            string username = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_USERNAME");
            string password = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_PWD");

            string csvUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_CSVURL");
            string loginUrl = Environment.GetEnvironmentVariable("VIR_PATIKAMAN_LOGINURL");

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                AcceptDownloads = true
            });
            var page = await context.NewPageAsync();

            await page.GotoAsync(loginUrl);
            await page.WaitForSelectorAsync("input[name='email']");

            await page.FillAsync("input[name='email']", username);
            await page.FillAsync("input[name='password']", password);

            await page.ClickAsync("button[type='submit']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.GotoAsync(csvUrl);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var downloadTask = page.WaitForDownloadAsync();

            await page.ClickAsync("text=Leadott rendelések CSV export");

            var download = await downloadTask;

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), csvFileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await download.SaveAsAsync(filePath);
            log.LogInformation($"CSV file downloaded to {filePath}");

            CsvToHtmlTableConverter converter = new CsvToHtmlTableConverter(log);
            string htmlOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "patikaman.html");
            //converter.GenerateTable_1(filePath, new DateTime(2025, 5, 6));
            converter.GenerateTable_1(filePath, DateTime.Today);
        }*/
        public void ExecuteTask()
        {
            log.LogInformation("Scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            DownloadCsv();

            stopwatch.Stop();
            log.LogInformation($"Elapsed Time: {stopwatch.Elapsed.Hours} hours, {stopwatch.Elapsed.Minutes} minutes, {stopwatch.Elapsed.Seconds} seconds");
        }
    }
}

using DailyOrdersEmail.services;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System;
using System.Diagnostics;

namespace DailyOrdersEmail.task
{
    public class PatikamanTask(MetricService metricService, ILogger<PatikamanTask> log) : ServiceTask
    {
        private static async Task Run()
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

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), download.SuggestedFilename);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await download.SaveAsAsync(filePath);
        }
        public void ExecuteTask()
        {
            log.LogInformation("Scheduled run started.");
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            Run().GetAwaiter().GetResult();

            stopwatch.Stop();
            log.LogInformation($"Elapsed Time: {stopwatch.Elapsed.Hours} hours, {stopwatch.Elapsed.Minutes} minutes, {stopwatch.Elapsed.Seconds} seconds");
        }
    }
}

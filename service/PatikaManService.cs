using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Timers;
using DailyOrdersEmail.task;

namespace DailyOrdersEmail.service
{
    public class PatikaManService : BackgroundService //ServiceBase
    {
        //private System.Timers.Timer timer;
        private DateTime lastExecutionDate;
        private readonly ILogger<PatikaManService> log;
        private readonly List<ServiceTask> tasks;

        public PatikaManService(ILogger<PatikaManService> logger, IEnumerable<ServiceTask> taskList)
        {
            lastExecutionDate = DateTime.MinValue;
            log = logger;
            tasks = taskList.ToList();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var heartbeatTask = Heartbeat(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                // Calculate the next 8 PM
                var now = DateTime.Now;
                var nextRun = now.Date.AddDays(now.Hour >= 18? 1 : 0).AddHours(18);

                // Skip Saturday and Sunday
                while (nextRun.DayOfWeek == DayOfWeek.Saturday || nextRun.DayOfWeek == DayOfWeek.Sunday)
                {
                    nextRun = nextRun.AddDays(1); // Move to next day until it's a weekday
                }

                var delay = nextRun - now;
 
                var readableDelay = $"{delay.Days} days, {delay.Hours} hours, {delay.Minutes} minutes, and {delay.Seconds} seconds";
                log.LogInformation($"Next Patikaman task scheduled to run in: {readableDelay}");

                try
                {
                    await Task.Delay(delay, stoppingToken);
                    await DailyTask(stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "An error occurred while scheduling the task.");
                }
            }

            await heartbeatTask;
        }

        // Custom start method for running in console
        public void StartAsConsole(string[] args)
        {
            try
            {
                foreach (var task in tasks)
                {
                    task.ExecuteTask();
                }
            }
            catch (Exception ex)
            {
                log.LogError($"Error: {ex}");
            }
        }

        private async Task Heartbeat(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    log.LogDebug("Heartbeat...");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "An error occurred in the heartbeat process.");
                }
            }
        }

        private async Task DailyTask(CancellationToken stoppingToken)
        {
            log.LogInformation("It is 8pm. Start the Patikaman CSV download tasks.");
            foreach (var task in tasks)
            {
                task.ExecuteTask();
            }

            await Task.CompletedTask;
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e)
        {
            DateTime now = DateTime.Now;
            // Check if the time is 3:00 AM and if the task has not been executed today
            if (now.Hour == 3 && now.Minute == 0 && lastExecutionDate.Date != now.Date)
            {
                log.LogInformation("It is 3am. Start the Faktur import tasks.");
                foreach (var task in tasks)
                {
                    task.ExecuteTask();
                }

                lastExecutionDate = now.Date;
            }
        }

        /*protected override void OnStop()
        {
            timer?.Stop();
            timer?.Dispose();
        }*/
    }
}


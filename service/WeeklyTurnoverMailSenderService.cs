using System;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using OrderEmail.task;

namespace OrderEmail.service
{
    public class WeeklyTurnoverMailSenderService : BackgroundService
    {
        private readonly List<ServiceTask> tasks;
        private readonly ILogger<WeeklyTurnoverMailSenderService> log;

        public WeeklyTurnoverMailSenderService(ILogger<WeeklyTurnoverMailSenderService> logger, IEnumerable<ServiceTask> taskList)
        {
            log = logger;
            tasks = taskList.ToList();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            log.LogInformation("WeeklyTurnoverMailSenderService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;

                    // Calculate next Friday 17:00
                    int daysUntilFriday =
                        ((int)DayOfWeek.Friday - (int)now.DayOfWeek + 7) % 7;

                    var nextRun = now.Date
                        .AddDays(daysUntilFriday)
                        .AddHours(17);

                    // If today is Friday and already past 17:00 → next week
                    if (daysUntilFriday == 0 && now >= nextRun)
                    {
                        nextRun = nextRun.AddDays(7);
                    }

                    var delay = nextRun - now;

                    log.LogInformation(
                        $"Next execution scheduled for {nextRun} (in {delay.TotalHours:F1} hours).");

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await ExecuteServiceTasks(stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    log.LogInformation("Service stopping due to cancellation.");
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "An error occurred in the scheduling loop.");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
            }

            log.LogInformation("WeeklyTurnoverMailSenderService stopped.");
        }

        private async Task ExecuteServiceTasks(CancellationToken stoppingToken)
        {
            log.LogInformation($"[{DateTime.Now}] Starting weekly turnover mail tasks.");

            foreach (var task in tasks)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    log.LogInformation("Service stopping. Aborting task execution.");
                    break;
                }

                try
                {
                    task.ExecuteTask();
                    log.LogInformation(
                        $"Task '{task.GetType().Name}' executed successfully.");
                }
                catch (Exception ex)
                {
                    log.LogError(
                        ex,
                        $"Error while executing task '{task.GetType().Name}'.");
                }
            }

            await Task.CompletedTask;
        }

    }
}

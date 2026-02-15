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

        private static readonly DayOfWeek RunDay = DayOfWeek.Sunday;
        private static readonly TimeSpan RunTime = new TimeSpan(14, 02, 0);

        public string GetInfo()
        {
            return $"{tasks.Count} tasks";
        }
        public WeeklyTurnoverMailSenderService(ILogger<WeeklyTurnoverMailSenderService> logger, IEnumerable<ServiceTask> taskList)
        {
            log = logger;
            log.LogInformation("WeeklyTurnoverMailSenderService instantiated.");
            tasks = taskList.ToList();
            log.LogInformation($"{tasks.Count} tasks");
        }

        private static DateTime GetNextRun(DateTime now, DayOfWeek runDay, TimeSpan runTime)
        {
            int daysUntilRun =
                ((int)runDay - (int)now.DayOfWeek + 7) % 7;

            DateTime nextRun = now.Date
                .AddDays(daysUntilRun)
                .Add(runTime);

            // If today is the run day but the time already passed → next week
            if (daysUntilRun == 0 && now >= nextRun)
            {
                nextRun = nextRun.AddDays(7);
            }

            return nextRun;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            log.LogInformation("WeeklyTurnoverMailSenderService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    DateTime nextRun = GetNextRun(now, RunDay, RunTime);

                    TimeSpan delay = nextRun - now;

                    log.LogInformation(
                        "Next weekly execution scheduled for {NextRun} (in {Hours:F1} hours).",
                        nextRun,
                        delay.TotalHours);

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

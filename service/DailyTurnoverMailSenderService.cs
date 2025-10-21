using System;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Collections.Generic;
using DailyOrdersEmail.task;
using System.Linq;

namespace DailyOrdersEmail.service
{
    public class DailyTurnoverMailSenderService : BackgroundService
    {
        private readonly List<ServiceTask> tasks;
        private readonly ILogger<DailyTurnoverMailSenderService> log;

        public DailyTurnoverMailSenderService(ILogger<DailyTurnoverMailSenderService> logger, IEnumerable<ServiceTask> taskList)
        {
            log = logger;
            tasks = taskList.ToList();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            log.LogInformation("DailyTurnoverMailSenderService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var nextRun = new DateTime(now.Year, now.Month, now.Day, 16, 55, 0);
                    if (now > nextRun)
                        nextRun = nextRun.AddDays(1);

                    while (nextRun.DayOfWeek == DayOfWeek.Saturday || nextRun.DayOfWeek == DayOfWeek.Sunday)
                        nextRun = nextRun.AddDays(1);

                    var delay = nextRun - now;
                    log.LogInformation($"Next execution scheduled for {nextRun} (in {delay.TotalMinutes:F0} minutes).");

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

            log.LogInformation("DailyTurnoverMailSenderService stopped.");
        }

        private async Task ExecuteServiceTasks(CancellationToken stoppingToken)
        {
            log.LogInformation($"[{DateTime.Now}] Starting daily turnover mail tasks.");

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
                    log.LogInformation($"Task '{task.GetType().Name}' executed successfully.");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"Error while executing task '{task.GetType().Name}'.");
                }
            }

            await Task.CompletedTask;
        }
    }
}

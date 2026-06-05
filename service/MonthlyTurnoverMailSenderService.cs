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
    public class MonthlyTurnoverMailSenderService : BackgroundService
    {
        private readonly List<ServiceTask> tasks;
        private readonly ILogger<MonthlyTurnoverMailSenderService> log;

        // TEST OVERRIDES (set to null in production)
        //private static readonly DayOfWeek? OverrideRunDay = DayOfWeek.Wednesday;
        //private static readonly TimeSpan? OverrideRunTime = new TimeSpan(17, 33, 0);
        private static readonly DayOfWeek? OverrideRunDay = null;
        private static readonly TimeSpan? OverrideRunTime = null;

        // Monthly default
        private static readonly TimeSpan MonthlyRunTime = new TimeSpan(17, 32, 0);

        public MonthlyTurnoverMailSenderService(
            ILogger<MonthlyTurnoverMailSenderService> logger,
            IEnumerable<ServiceTask> taskList)
        {
            log = logger;
            log.LogInformation("MonthlyTurnoverMailSenderService instantiated.");

            tasks = taskList.ToList();
            log.LogInformation("{TaskCount} tasks registered.", tasks.Count);
        }

        private static DateTime GetNextRun(DateTime now)
        {
            if (OverrideRunDay.HasValue && OverrideRunTime.HasValue)
            {
                return GetNextTestRun(
                    now,
                    OverrideRunDay.Value,
                    OverrideRunTime.Value);
            }

            return GetNextMonthlyRun(now);
        }

        private static DateTime GetNextTestRun(
            DateTime now,
            DayOfWeek runDay,
            TimeSpan runTime)
        {
            int daysUntilRun =
                ((int)runDay - (int)now.DayOfWeek + 7) % 7;

            DateTime nextRun = now.Date
                .AddDays(daysUntilRun)
                .Add(runTime);

            if (daysUntilRun == 0 && now >= nextRun)
            {
                nextRun = nextRun.AddDays(7);
            }

            return nextRun;
        }

        private static DateTime GetNextMonthlyRun(DateTime now)
        {
            DateTime candidate = GetAdjustedMonthEnd(now.Year, now.Month);

            if (now >= candidate)
            {
                DateTime nextMonth = now.AddMonths(1);
                candidate = GetAdjustedMonthEnd(nextMonth.Year, nextMonth.Month);
            }

            return candidate;
        }

        private static DateTime GetNextRunAfterExecution(DateTime now)
        {
            if (OverrideRunDay.HasValue && OverrideRunTime.HasValue)
            {
                return GetNextTestRunAfterExecution(
                    now,
                    OverrideRunDay.Value,
                    OverrideRunTime.Value);
            }

            return GetNextMonthlyRunAfterExecution(now);
        }

        private static DateTime GetNextTestRunAfterExecution(
            DateTime now,
            DayOfWeek runDay,
            TimeSpan runTime)
        {
            int daysUntilRun =
                ((int)runDay - (int)now.DayOfWeek + 7) % 7;

            DateTime nextRun = now.Date
                .AddDays(daysUntilRun)
                .Add(runTime);

            if (daysUntilRun == 0)
            {
                nextRun = nextRun.AddDays(7);
            }

            return nextRun;
        }

        private static DateTime GetNextMonthlyRunAfterExecution(DateTime now)
        {
            DateTime candidate = GetAdjustedMonthEnd(now.Year, now.Month);

            if (now.Date == candidate.Date && now <= candidate)
            {
                DateTime nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                return GetAdjustedMonthEnd(nextMonth.Year, nextMonth.Month);
            }

            return GetNextMonthlyRun(now);
        }

        private static DateTime GetAdjustedMonthEnd(int year, int month)
        {
            DateTime lastDay = new DateTime(
                year,
                month,
                DateTime.DaysInMonth(year, month));

            if (lastDay.DayOfWeek == DayOfWeek.Saturday)
                lastDay = lastDay.AddDays(-1);
            else if (lastDay.DayOfWeek == DayOfWeek.Sunday)
                lastDay = lastDay.AddDays(-2);

            return lastDay.Date.Add(MonthlyRunTime);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            log.LogInformation("MonthlyTurnoverMailSenderService started.");

            var scheduleAfterExecution = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    DateTime nextRun = scheduleAfterExecution
                        ? GetNextRunAfterExecution(now)
                        : GetNextRun(now);
                    scheduleAfterExecution = false;

                    TimeSpan delay = nextRun - now;

                    log.LogInformation(
                        "Next monthly execution scheduled for {NextRun} (in {Days:F1} days).",
                        nextRun,
                        delay.TotalDays);

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    await ExecuteServiceTasks(stoppingToken);
                    scheduleAfterExecution = true;
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

            log.LogInformation("MonthlyTurnoverMailSenderService stopped.");
        }

        private async Task ExecuteServiceTasks(CancellationToken stoppingToken)
        {
            log.LogInformation("[{Now}] Starting monthly turnover mail tasks.", DateTime.Now);

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
                        "Task '{TaskName}' executed successfully.",
                        task.GetType().Name);
                }
                catch (Exception ex)
                {
                    log.LogError(
                        ex,
                        "Error while executing task '{TaskName}'.",
                        task.GetType().Name);
                }
            }

            await Task.CompletedTask;
        }
    }
}
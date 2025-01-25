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
    public class MailSenderService : BackgroundService
    {
        private readonly List<ServiceTask> tasks;
        private readonly ILogger<MailSenderService> log;
        private readonly TimeSpan interval = TimeSpan.FromMinutes(10); // Execution interval

        public MailSenderService(ILogger<MailSenderService> logger, IEnumerable<ServiceTask> taskList)
        {
            log = logger;
            tasks = taskList.ToList();
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExecuteServiceTask(stoppingToken);
                    log.LogInformation("Tasks executed successfully. Waiting for the next interval...");
                }
                catch (TaskCanceledException)
                {
                    log.LogInformation("Task was canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "An error occurred while executing tasks.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    log.LogInformation("Service stopping.");
                    break;
                }
            }
        }

        private async Task ExecuteServiceTask(CancellationToken stoppingToken)
        {
            log.LogInformation("Starting the mail checker service tasks.");
            foreach (var task in tasks)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    log.LogInformation("Service stopping. Aborting tasks execution.");
                    break;
                }

                try
                {
                    task.ExecuteTask();
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"An error occurred while executing task '{task.GetType().Name}'.");
                }
            }
            await Task.CompletedTask;
        }
    }
}
using DailyOrdersEmail.service;
using DailyOrdersEmail.services;
using DailyOrdersEmail.task;
using DailyOrdersEmail.util;
using log4net;
using log4net.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DailyOrdersEmail
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly String serviceName = "DailyMailService";
        private static readonly String serviceVersion = "1.0.0";
        private static void ConfigureServices(HostApplicationBuilder appBuilder)
        {
            appBuilder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "VIR daily mail sender service.";
            });

            appBuilder.Services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        .AddSource(serviceName)
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                            serviceName: serviceName,
                            serviceVersion: serviceVersion))
                        .AddOtlpExporter();
                })
                .WithMetrics(builder =>
                {
                    builder
                        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                            serviceName: serviceName,
                            serviceVersion: serviceVersion))
                        .AddMeter(serviceName)
                        .AddRuntimeInstrumentation()
                        .AddOtlpExporter();
                    //.AddConsoleExporter();
                });

            appBuilder.Services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddOpenTelemetry(options =>
                {
                    options.IncludeScopes = true;
                    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                        serviceName: serviceName,
                        serviceVersion: serviceVersion));
                    options.AddOtlpExporter();
                });

                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string log4NetConfigFilePath = Path.Combine(exeDirectory, "log4net.config");
                builder.AddLog4Net(log4NetConfigFilePath);
            });

            var assembly = Assembly.GetExecutingAssembly();

            var serviceTasks = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ServiceTask).IsAssignableFrom(t));
            foreach (var task in serviceTasks)
            {
                appBuilder.Services.AddTransient(typeof(ServiceTask), task);
            }

            appBuilder.Services.AddSingleton<MetricService>();

            appBuilder.Services.AddHostedService(sp =>
                new MailSenderService(sp.GetRequiredService<ILogger<MailSenderService>>(),
                sp.GetRequiredService<IEnumerable<ServiceTask>>()));

            appBuilder.Services.AddSingleton(sp =>
                new MailSenderService(sp.GetRequiredService<ILogger<MailSenderService>>(),
                sp.GetRequiredService<IEnumerable<ServiceTask>>()));

        }
        static void Main(string[] args)
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string log4NetConfigFilePath = Path.Combine(exeDirectory, "log4net.config");
            XmlConfigurator.Configure(new FileInfo(log4NetConfigFilePath));
            log.Info("Application started.");

            HostApplicationBuilder appBuilder = Host.CreateApplicationBuilder(args);

            ConfigureServices(appBuilder);

            var serviceProvider = appBuilder.Services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogDebug("Framework: " + FRWK.GetEnvironmentVersion() + " " + FRWK.GetTargetFrameworkName() + " " + FRWK.GetFrameworkDescription());


            if (Environment.UserInteractive)
            {
                var parameter = string.Concat(args);
                switch (parameter)
                {
                    case "--install":
                        ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                        break;
                    case "--uninstall":
                        ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
                        break;
                    default:
                        // Run as a console app for debugging

                        using (logger.BeginScope("Console mode"))
                        {
                            logger.LogInformation("Starting the service in interactive mode.");

                            var mailService = serviceProvider.GetRequiredService<MailSenderService>();
                            mailService.StartAsConsole(null);

                        }
                        break;
                }
            }
            else
            {
                logger.LogInformation("Starting the service as a Windows Service...");
                IHost host = appBuilder.Build();
                host.Run();
            }

            serviceProvider.Dispose();
        }
    }
}

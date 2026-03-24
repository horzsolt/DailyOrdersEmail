using OrderEmail.task;
using OrderEmail.util;
using log4net.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderEmail.service;
using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DailyOrdersEmail
{
    class Program
    {
        private static readonly String serviceName = "VIR daily mail sender service";
        private static readonly String serviceVersion = "1.0.0";
        private static void ConfigureServices(HostApplicationBuilder appBuilder)
        {

            if (OperatingSystem.IsWindows() && !Environment.UserInteractive)
            {
                appBuilder.Services.AddWindowsService(options =>
                {
                    options.ServiceName = serviceName;
                });
            }

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

            var serviceProvider = appBuilder.Services.BuildServiceProvider();
            var log = serviceProvider.GetRequiredService<ILogger<Program>>();

            var assembly = Assembly.GetExecutingAssembly();

            var serviceTasks = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ServiceTask).IsAssignableFrom(t));
            foreach (var task in serviceTasks)
            {
                appBuilder.Services.AddTransient(typeof(ServiceTask), task);
            }

            appBuilder.Services.AddSingleton<MetricService>(provider =>
            {
                var meterFactory = provider.GetRequiredService<IMeterFactory>();
                var logger = provider.GetRequiredService<ILogger<MetricService>>();

                return new MetricService(meterFactory, logger, serviceName, serviceVersion);
            });

            // Production version

            try
            {
                appBuilder.Services.AddHostedService(sp =>
                 new MailSenderService(sp.GetRequiredService<ILogger<MailSenderService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                     .Where(
                     t => (t.GetType().GetCustomAttribute<CheckNewOrderTaskAttribute>() != null)
                     )));

                appBuilder.Services.AddHostedService(sp =>
                 new DailyTurnoverMailSenderService(sp.GetRequiredService<ILogger<DailyTurnoverMailSenderService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                     .Where(
                     t => (t.GetType().GetCustomAttribute<DailyOrderSummaryTaskAttribute>() != null)
                     )));

                appBuilder.Services.AddHostedService(sp =>
                {
                    var service = new WeeklyTurnoverMailSenderService(
                        sp.GetRequiredService<ILogger<WeeklyTurnoverMailSenderService>>(),
                        sp.GetRequiredService<IEnumerable<ServiceTask>>()
                            .Where(t =>
                                t.GetType().GetCustomAttribute<WeeklyOrderSummaryTaskAttribute>() != null)
                    );

                    return service;
                });

                appBuilder.Services.AddHostedService(sp =>
                {
                    var service = new MonthlyTurnoverMailSenderService(
                        sp.GetRequiredService<ILogger<MonthlyTurnoverMailSenderService>>(),
                        sp.GetRequiredService<IEnumerable<ServiceTask>>()
                            .Where(t =>
                                t.GetType().GetCustomAttribute<MonthlyOrderSummaryTaskAttribute>() != null)
                    );

                    return service;
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex.ToString());
            }

            /*appBuilder.Services.AddHostedService(sp =>
                new WeeklyTurnoverMailSenderService(sp.GetRequiredService<ILogger<WeeklyTurnoverMailSenderService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                    .Where(
                    t => (t.GetType().GetCustomAttribute<WeeklyOrderSummaryTaskAttribute>() != null)
                )));*/

            // End production version

            // For testing
            /*appBuilder.Services.AddTransient(sp =>
                new MailSenderService(sp.GetRequiredService<ILogger<MailSenderService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                    .Where(t => t.GetType().GetCustomAttribute<DailyScriptorOrderSummaryTaskAttribute>() != null)));

            appBuilder.Services.AddTransient(sp =>
                new DailyTurnoverMailSenderService(sp.GetRequiredService<ILogger<DailyTurnoverMailSenderService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                    .Where(t => t.GetType().GetCustomAttribute<DailyScriptorOrderSummaryTaskAttribute>() != null)));
            */
            // End testing.
            appBuilder.Services.AddSingleton(sp =>
                new PatikaManService(sp.GetRequiredService<ILogger<PatikaManService>>(), sp.GetRequiredService<IEnumerable<ServiceTask>>()
                    .Where(t => t.GetType().GetCustomAttribute<PatikamanTaskAttribute>() != null)));
        }

        static void Main(string[] args)
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string log4NetConfigFilePath = Path.Combine(exeDirectory, "log4net.config");
            XmlConfigurator.Configure(new FileInfo(log4NetConfigFilePath));

            var builder = Host.CreateApplicationBuilder(args);

            ConfigureServices(builder);

            var host = builder.Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Application started.");
            logger.LogDebug("Framework: " + FRWK.GetEnvironmentVersion() + " " + FRWK.GetTargetFrameworkName() + " " + FRWK.GetFrameworkDescription());

            host.Run();
        }
        static void _Main(string[] args)
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string log4NetConfigFilePath = Path.Combine(exeDirectory, "log4net.config");
            XmlConfigurator.Configure(new FileInfo(log4NetConfigFilePath));
            HostApplicationBuilder appBuilder = Host.CreateApplicationBuilder(args);

            ConfigureServices(appBuilder);

            var serviceProvider = appBuilder.Services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Application started.");
            logger.LogDebug("Framework: " + FRWK.GetEnvironmentVersion() + " " + FRWK.GetTargetFrameworkName() + " " + FRWK.GetFrameworkDescription());

            var hostedServices = appBuilder.Services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

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

                            var testService = serviceProvider.GetRequiredService<MailSenderService>();
                            testService.StartAsConsole(null);

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

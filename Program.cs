using log4net;
using log4net.Config;
using log4net.Repository.Hierarchy;
using System;
using System.Configuration.Install;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Timers;

namespace DailyOrdersEmail
{
    class Program
    {
        private static Timer checkTimer;
        private static DateTime lastCheckTime;
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string log4NetConfigFilePath = Path.Combine(exeDirectory, "log4net.config");
            XmlConfigurator.Configure(new FileInfo(log4NetConfigFilePath));
            log.Info("Application started.");

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

                        log.Info("Running as a console application for debugging.");
                        MailSenderService service = new MailSenderService();
                        service.StartAsConsole(args);
                        break;
                }
            }
            else
            {
                // Run as a Windows Service

                log.Info("Running as a Windows Service.");

                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new MailSenderService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}

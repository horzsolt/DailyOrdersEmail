using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Timers;
using log4net;
using System.Reflection;
using System.ServiceProcess;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace DailyOrdersEmail
{
    public class MailSenderService : ServiceBase
    {
        private Timer checkTimer;
        private readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public MailSenderService()
        {
            
        }

        // Custom start method for running in console
        public void StartAsConsole(string[] args)
        {
            try
            {
                NewOrdersHandler newOrdersHandler = new NewOrdersHandler();
                newOrdersHandler.StartCheck_Scheduled(null, null);
            }
            catch (Exception ex)
            {
                log.Error($"Error: {ex}");
            }
        }

        protected override void OnStart(string[] args)
        {
            log.Debug("Service OnStart called.");

            NewOrdersHandler newOrdersHandler = new NewOrdersHandler();
            QueryLoggerHandler queryLoggerHandler = new QueryLoggerHandler();

            checkTimer = new Timer(1800000);
            checkTimer.Elapsed += newOrdersHandler.StartCheck_Scheduled;
            checkTimer.AutoReset = true;
            checkTimer.Enabled = true;

            Timer checkTimer2 = new Timer(300000);
            checkTimer2.Elapsed += (sender, e) =>
            {
                Task.Run(() => queryLoggerHandler.QueryLogger_Scheduled(sender, e));
            };
            checkTimer2.AutoReset = true;
            checkTimer2.Enabled = true;
        }

        private void ScheduleStartCheck(NewOrdersHandler newOrdersHandler)
        {
            DateTime now = DateTime.Now;
            int minutes = now.Minute;
            int seconds = now.Second;
            int milliseconds = now.Millisecond;

            // Calculate the initial delay until the next 30-minute mark
            int minutesUntilNextInterval = (minutes < 30) ? (30 - minutes) : (60 - minutes);
            double initialDelay = (minutesUntilNextInterval * 60 - seconds) * 1000 - milliseconds;

            // Set up the timer to run at the full hour and half-hour intervals
            Timer checkTimer = new Timer(initialDelay);
            checkTimer.Elapsed += (sender, e) =>
            {
                newOrdersHandler.StartCheck_Scheduled(sender, e);
                // After the first run, set the interval to 30 minutes (1800000 ms)
                checkTimer.Interval = 1800000;
                checkTimer.AutoReset = true;
            };
            checkTimer.AutoReset = false; // Run once for the initial scheduling
            checkTimer.Enabled = true;
        }

        protected override void OnStop()
        {
            log.Info("Service OnStop called.");
        }
    }

}

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
            checkTimer2.Elapsed += queryLoggerHandler.QueryLogger_Scheduled;
            checkTimer2.AutoReset = true;
            checkTimer2.Enabled = true;
        }

        protected override void OnStop()
        {
            log.Info("Service OnStop called.");
        }
    }

}

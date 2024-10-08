using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DailyOrdersEmail
{
    public class Configuration
    {
        public string MailServer { get; set; } = string.Empty;
        public string MailSecrecy { get; set; } = string.Empty;
        public string MailPassword { get; set; } = string.Empty;
        public string MailSendTo { get; set; } = string.Empty;
        public string MailSendFrom { get; set; } = string.Empty;
        public string MailSaveToFolder { get; set; } = string.Empty;
        public string MailSelectStatement { get; set; } = string.Empty;
        public int MailRetentionDays { get; set; } = 0;
        public DateTime LastCheckTime { get; set; } = DateTime.Now;
    }

}

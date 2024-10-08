using log4net;
using log4net.Config;
using System;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Reflection;
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
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            log.Info("Application started.");

            lastCheckTime = DateTime.Now;

            /*checkTimer = new Timer(1800000);
            checkTimer.Elapsed += CheckForNewRecords;
            checkTimer.AutoReset = true;
            checkTimer.Enabled = true;*/

            CheckForNewRecords(null, null);

            Console.WriteLine("Press [Enter] to exit the program...");
            Console.ReadLine();
        }

        private static void CheckForNewRecords(object source, ElapsedEventArgs e)
        {
            try
            {
                string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                                          $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                                          $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                                          $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};";

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    string query = "SELECT TOP 1 * FROM dbo.DailyOrderMailConfig";
                    Configuration config = new Configuration();

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                config.LastCheckTime = reader.IsDBNull(0) ? (DateTime.Now) : reader.GetDateTime(0);
                                config.MailServer = reader.IsDBNull(1) ? String.Empty : reader.GetString(1);
                                config.MailSecrecy = reader.IsDBNull(2) ? String.Empty : reader.GetString(2);
                                config.MailPassword = reader.IsDBNull(3) ? String.Empty : reader.GetString(3);
                                config.MailSendTo = reader.IsDBNull(4) ? String.Empty : reader.GetString(4);
                                config.MailSaveToFolder = reader.IsDBNull(5) ? String.Empty : reader.GetString(5);
                                config.MailSelectStatement = reader.IsDBNull(6) ? String.Empty : reader.GetString(6);
                                config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);

                                log.Debug($"Last check time: {config.LastCheckTime}");
                                log.Debug($"Select stmt: {config.MailSelectStatement}");
                            }
                            else
                            {
                                log.Error("No records found in the DailyOrderMailConfig table.");
                                return;
                            }
                        }
                    }
                    
                    query = config.MailSelectStatement;
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@timestamp", config.LastCheckTime);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            DataTable dataTable = new DataTable();
                            dataTable.Load(reader);

                            if (dataTable.Rows.Count > 0)
                            {
                                GenerateHtmlEmail(dataTable, config);
                            } else
                            {
                                log.Info("No new records found.");
                            }
                        }
                    }
                }

                // Update last check time
                lastCheckTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                log.Error($"Error: {ex.Message}");
            }
        }

        private static void GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {

            int CID = 0;
            string agentName = String.Empty;

            int actual_CID = 0;
            string actual_agentName = String.Empty;

            int sum_Rend_Unit = 0;
            int sum_Rabatt = 0;
            int sum_Forgalom = 0;

            StringBuilder htmlBuilder = new StringBuilder();

            foreach (DataRow row in dataTable.Rows)
            {
                actual_CID = row["CID"] is DBNull ? 0 : (int)row["CID"];
                actual_agentName = row["Nev"] is DBNull ? String.Empty : (string)row["Nev"];

                if ((actual_CID != CID) || (actual_agentName != agentName))
                {
                    // Add summary

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(sum_Rend_Unit)}</td>");
                    htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(sum_Rabatt)}</td>");
                    htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(sum_Forgalom)}</td>");
                    htmlBuilder.Append("</tr>");

                    htmlBuilder.Append("</table>");

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, CID.ToString() + "_" + agentName + ".html"));
                    htmlBuilder.Clear();
                    CID = actual_CID;
                    agentName = actual_agentName;
                    sum_Forgalom = 0;
                    sum_Rabatt = 0;
                    sum_Rend_Unit = 0;

                    AddHeader(htmlBuilder, row);
                }

                htmlBuilder.Append("<tr>");
                htmlBuilder.Append($"<td>{Util.GetValueOrDefault<string>(row["Termek"])}</td>");
                htmlBuilder.Append($"<td>{Util.GetValueOrDefault<float>(row["Kedv_Sz"])}</td>");
                htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(row["Rend_Unit"])}</td>");
                htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(row["Rabatt"])}</td>");
                htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(row["Forgalom"])}</td>");
                htmlBuilder.Append("</tr>");

                sum_Rend_Unit += Util.GetValueOrDefault<int>(row["Rend_Unit"]);
                sum_Rabatt += Util.GetValueOrDefault<int>(row["Rabatt"]);
                sum_Forgalom += Util.GetValueOrDefault<int>(row["Forgalom"]);
            }
        }

        private static void AddHeader(StringBuilder htmlBuilder, DataRow row)
        {
            // Add header
            htmlBuilder.Append("<table border='1'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Rend_Datum</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<DateTime>(row["Rend_Datum"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Nev</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<string>(row["Nev"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>CID</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<int>(row["CID"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Ugyfel</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<string>(row["Ugyfel"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Helyseg</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<string>(row["Helyseg"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Nagyker</td>");
            htmlBuilder.Append($"<td>{Util.GetValueOrDefault<string>(row["Nagyker"])}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("</table>");

            htmlBuilder.Append("<table border='1'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<th>Termek</th>");
            htmlBuilder.Append($"<th>Kedv_Sz</th>");
            htmlBuilder.Append($"<th>Rend_Unit</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
        }

        private static void SendEmail(string htmlContent)
        {
            try
            {
                MailMessage mail = new MailMessage();
                SmtpClient smtpServer = new SmtpClient("your-smtp-server.com");

                mail.From = new MailAddress("youremail@domain.com");
                mail.To.Add("recipient@domain.com");
                mail.Subject = "New Records Notification";
                mail.IsBodyHtml = true;
                mail.Body = htmlContent;

                smtpServer.Port = 587; // or 25
                smtpServer.Credentials = new NetworkCredential("your-email", "your-password");
                smtpServer.EnableSsl = true;

                smtpServer.Send(mail);
                Console.WriteLine("Email sent successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send email: {ex.Message}");
            }
        }
    }
}

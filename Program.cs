﻿using log4net;
using log4net.Config;
using log4net.Repository.Hierarchy;
using System;
using System.Data;
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

            /*checkTimer = new Timer(1800000);
            checkTimer.Elapsed += CheckForNewRecords;
            checkTimer.AutoReset = true;
            checkTimer.Enabled = true;*/

            try
            {
                CheckForNewRecords(null, null);
                Util.RemoveOldFiles(Environment.GetEnvironmentVariable("VIR_MAIL_SAVE_TO_FOLDER"), 30);
            }
            catch (Exception ex)
            {
                log.Error($"Error: {ex}");
            }

            Console.WriteLine("Press [Enter] to exit the program...");
            Console.ReadLine();
        }

        private static void CheckForNewRecords(object source, ElapsedEventArgs e)
        {

            string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                                      $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                                      $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                                      $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};" +
                                      "Connection Timeout=500;";

            log.Debug($"Connection string: {connectionString}");

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
                            config.LastCheckTime = reader.IsDBNull(0) ? (DateTime.Now.AddHours(-1)) : reader.GetDateTime(0);
                            config.MailServer = reader.IsDBNull(1) ? String.Empty : reader.GetString(1).Trim();
                            config.MailSecrecy = reader.IsDBNull(2) ? String.Empty : reader.GetString(2).Trim();
                            config.MailPassword = reader.IsDBNull(3) ? String.Empty : reader.GetString(3).Trim();
                            config.MailSendTo = reader.IsDBNull(4) ? String.Empty : reader.GetString(4).Trim();
                            config.MailSaveToFolder = reader.IsDBNull(5) ? String.Empty : reader.GetString(5).Trim();
                            config.MailSelectStatement = reader.IsDBNull(6) ? String.Empty : reader.GetString(6).Trim();
                            config.MailRetentionDays = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                            config.MailSendFrom = reader.IsDBNull(8) ? String.Empty : reader.GetString(8).Trim();

                            config.LastCheckTime = new DateTime(2024, 10, 8, 15, 30, 0);
                            log.Debug($"Last check time: {config.LastCheckTime}");
                            log.Debug($"Select stmt: {config.MailSelectStatement}");
                            log.Debug($"SaveTo folder: {config.MailSaveToFolder}");
                        }
                        else
                        {
                            log.Error("No records found in the DailyOrderMailConfig table.");
                            return;
                        }
                    }
                }

                query = config.MailSelectStatement;
                lastCheckTime = DateTime.Now;

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.CommandTimeout = 500;
                    command.Parameters.AddWithValue("@timestamp", config.LastCheckTime);

                    log.Debug($"Executing query: {command.CommandText}");

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        DataTable dataTable = new DataTable();
                        dataTable.Load(reader);
                        GenerateHtmlEmail(dataTable, config);
                    }
                }

                query = "UPDATE dbo.DailyOrderMailConfig SET last_check = @timestamp";

                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@timestamp", lastCheckTime);
                    int rowsAffected = command.ExecuteNonQuery();
                    log.Debug($"Rows affected after updating the lastCheckTime: {rowsAffected}");
                }
            }
        }

        private static void GenerateHtmlEmail(DataTable dataTable, Configuration config)
        {
            if (dataTable.Rows.Count == 0)
            {
                log.Info("No records found.");
                return;
            }

            int CID = dataTable.Rows[0].Field<int>("CID");
            string agentName = dataTable.Rows[0].Field<string>("Nev");

            int actual_CID = 0;
            string actual_agentName = String.Empty;

            int sum_Rend_Unit = 0;
            int sum_Rabatt = 0;
            double sum_Forgalom = 0;

            StringBuilder htmlBuilder = new StringBuilder();
            AddHeader(htmlBuilder, dataTable.Rows[0]);

            foreach (DataRow row in dataTable.Rows)
            {
                actual_CID = row["CID"] is DBNull ? 0 : row.Field<int>("CID");
                actual_agentName = row["Nev"] is DBNull ? String.Empty : row.Field<string>("Nev");

                int index = dataTable.Rows.IndexOf(row);

                log.Debug($"Actual_CID: {actual_CID}");
                log.Debug($"Actual_AgentName: {actual_agentName}");
                log.Debug($"{index.ToString()}/{dataTable.Rows.Count}");

                if ((actual_CID != CID) || (actual_agentName != agentName))
                {
                    // Add summary
                    log.Debug($"Add summary for: {CID}, {agentName}. Row index: {index.ToString()}/{dataTable.Rows.Count}");
                    
                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Forgalom}</b></td>");
                    htmlBuilder.Append("</tr>");

                    htmlBuilder.Append("</table>");
                    htmlBuilder.Append("</html>");

                    string timeStamp = Util.RemoveSpecialCharsFromDateTime(DateTime.Now);
                    string fileName = index + "_" + CID.ToString() + "_" + agentName + "_" + timeStamp + ".html";
                    string subject = $"Napi rendelési értesítő {CID}, {agentName}-{timeStamp}";

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, fileName));
                    SendEmail(htmlBuilder.ToString(), config, subject);
                    htmlBuilder.Clear();

                    CID = actual_CID;
                    agentName = actual_agentName;
                    sum_Forgalom = 0;
                    sum_Rabatt = 0;
                    sum_Rend_Unit = 0;

                    AddHeader(htmlBuilder, row);
                }

                if (index == dataTable.Rows.Count - 1)
                {
                    // Add summary
                    log.Debug($"[END]: Add summary for: {CID}, {agentName}. Row index: {index.ToString()}/{dataTable.Rows.Count}");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td align='left'>{row["Termek"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Rabatt"]}</td>");
                    htmlBuilder.Append($"<td align='right'>{row["Forgalom"]}</td>");
                    htmlBuilder.Append("</tr>");

                    sum_Rend_Unit += row.Field<int>("Rend_Unit");
                    sum_Rabatt += row.Field<int>("Rabatt");
                    sum_Forgalom += row.Field<double>("Forgalom");

                    htmlBuilder.Append("<tr>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rend_Unit}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Rabatt}</b></td>");
                    htmlBuilder.Append($"<td align='right'><b>{sum_Forgalom}</b></td>");
                    htmlBuilder.Append("</tr>");

                    htmlBuilder.Append("</table>");
                    htmlBuilder.Append("</html>");

                    Util.SaveStringBuilderToFile(htmlBuilder, Path.Combine(config.MailSaveToFolder, index + "_" + CID.ToString() + "_" + agentName + ".html"));
                    htmlBuilder.Clear();
                }

                htmlBuilder.Append("<tr>");
                htmlBuilder.Append($"<td align='left'>{row["Termek"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Kedv_Sz"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rend_Unit"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Rabatt"]}</td>");
                htmlBuilder.Append($"<td align='right'>{row["Forgalom"]}</td>");
                htmlBuilder.Append("</tr>");

                sum_Rend_Unit += row.Field<int>("Rend_Unit");
                sum_Rabatt += row.Field<int>("Rabatt");
                sum_Forgalom += row.Field<double>("Forgalom");
            }
        }

        private static void AddHeader(StringBuilder htmlBuilder, DataRow row)
        {
            log.Debug($"Add header for {row.Field<string>("Nev")}");

            htmlBuilder.Append("<!DOCTYPE html>");
            htmlBuilder.Append("<html>");
            htmlBuilder.Append("<table border='1' style='border-collapse: collapse;padding: 20px;'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Rendelés ideje</td>");
            htmlBuilder.Append($"<td>{row.Field<DateTime>("Rogzitve")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Név</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Nev")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>CID</td>");
            htmlBuilder.Append($"<td align='right'>{row.Field<int>("CID")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Ügyfél</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Ugyfel")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Helység</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Helyseg")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<td>Nagyker</td>");
            htmlBuilder.Append($"<td>{row.Field<string>("Nagyker")}</td>");
            htmlBuilder.Append("</tr>");

            htmlBuilder.Append("</table>");

            htmlBuilder.Append("<table border='1' style='border-collapse: collapse;padding: 20px;'>");
            htmlBuilder.Append("<tr>");
            htmlBuilder.Append($"<th>Termék</th>");
            htmlBuilder.Append($"<th>Kedv. %</th>");
            htmlBuilder.Append($"<th>Rendelt menny.</th>");
            htmlBuilder.Append($"<th>Rabatt</th>");
            htmlBuilder.Append($"<th>Forgalom</th>");
            htmlBuilder.Append("</tr>");
        }

        private static void SendEmail(string htmlContent, Configuration config, string subject)
        {
            try
            {
                log.Debug($"Sending email: {subject} using {config.MailServer}:587");
                MailMessage mail = new MailMessage();
                SmtpClient smtpClient = new SmtpClient(config.MailServer);

                mail.From = new MailAddress(config.MailSendFrom);
                mail.To.Add("horzsolt2006@gmail.com");
                mail.Subject = subject;
                mail.IsBodyHtml = true;
                mail.Body = htmlContent;

                smtpClient.Port = 587;
                //smtpServer.Credentials = new NetworkCredential("your-email", "your-password");
                smtpClient.EnableSsl = true;

                smtpClient.Send(mail);
                log.Debug($"Email sent successfully: {subject}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to send email: {ex}");
                throw;
            }
        }
    }
}

using System;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Timers;

namespace DailyOrdersEmail
{
    class Program
    {
        private static Timer checkTimer;
        private static DateTime lastCheckTime;

        static void Main(string[] args)
        {
            // Set the last check time to when the application starts
            lastCheckTime = DateTime.Now;

            // Set up and start the timer (check every 5 minutes)
            checkTimer = new Timer(300000); // 5 minutes in milliseconds
            checkTimer.Elapsed += CheckForNewRecords;
            checkTimer.AutoReset = true;
            checkTimer.Enabled = true;

            Console.WriteLine("Press [Enter] to exit the program...");
            Console.ReadLine();
        }

        private static void CheckForNewRecords(object source, ElapsedEventArgs e)
        {
            try
            {
                // Fetch connection details from environment variables
                string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                                          $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                                          $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                                          $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};";

                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    // Query to get new records since last check
                    string query = "SELECT * FROM YourTable WHERE RecordDate > @LastCheck";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@LastCheck", lastCheckTime);

                        SqlDataAdapter adapter = new SqlDataAdapter(command);
                        DataTable newRecords = new DataTable();
                        adapter.Fill(newRecords);

                        if (newRecords.Rows.Count > 0)
                        {
                            // Generate the HTML email content
                            string htmlEmailContent = GenerateHtmlEmail(newRecords);

                            // Send the email with the new records
                            SendEmail(htmlEmailContent);
                        }
                    }
                }

                // Update last check time
                lastCheckTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static string GenerateHtmlEmail(DataTable newRecords)
        {
            StringBuilder htmlBuilder = new StringBuilder();

            htmlBuilder.Append("<h3>New Records:</h3>");
            htmlBuilder.Append("<table border='1'>");
            htmlBuilder.Append("<tr>");

            // Add headers
            foreach (DataColumn column in newRecords.Columns)
            {
                htmlBuilder.Append($"<th>{column.ColumnName}</th>");
            }

            htmlBuilder.Append("</tr>");

            // Add rows
            foreach (DataRow row in newRecords.Rows)
            {
                htmlBuilder.Append("<tr>");
                foreach (var item in row.ItemArray)
                {
                    htmlBuilder.Append($"<td>{item}</td>");
                }
                htmlBuilder.Append("</tr>");
            }

            htmlBuilder.Append("</table>");
            return htmlBuilder.ToString();
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

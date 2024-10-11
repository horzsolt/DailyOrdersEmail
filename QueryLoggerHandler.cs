using log4net;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Timers;

namespace DailyOrdersEmail
{
    public class QueryLoggerHandler
    {
        private static readonly ILog log = LogManager.GetLogger("SecondLogger");

        public void QueryLogger_Scheduled(object source, ElapsedEventArgs e)
        {
            log.Debug($"-- QueryLogger -- started.");
            string connectionString = $"Server={Environment.GetEnvironmentVariable("VIR_SQL_SERVER_NAME")};" +
                          $"Database={Environment.GetEnvironmentVariable("VIR_SQL_DATABASE")};" +
                          $"User Id={Environment.GetEnvironmentVariable("VIR_SQL_USER")};" +
                          $"Password={Environment.GetEnvironmentVariable("VIR_SQL_PASSWORD")};" +
                          "Connection Timeout=500;";

            try
            {

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
                                config.MailSelectStatement = reader.IsDBNull(6) ? String.Empty : reader.GetString(6).Trim();
                            }
                        }
                    }

                    query = config.MailSelectStatement;

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.CommandTimeout = 500;
                        command.Parameters.AddWithValue("@timestamp", config.LastCheckTime);

                        //string loggableQuery = query.Replace("@timestamp", $"'{config.LastCheckTime:yyyy-MM-dd HH:mm:ss}'");
                        log.Debug($"-- QueryLogger -- last check time: {config.LastCheckTime:yyyy-MM-dd HH:mm:ss}");

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            DataTable dataTable = new DataTable();
                            dataTable.Load(reader);

                            if (dataTable.Rows.Count == 0)
                            {
                                log.Debug("-- QueryLogger -- No records found.");
                                return;
                            }

                            foreach (DataRow row in dataTable.Rows)
                            {
                                string CID = row["CID"] is DBNull ? "0" : row.Field<int>("CID").ToString();
                                string agentName = row["Nev"] is DBNull ? String.Empty : row.Field<string>("Nev");
                                string rendAzon = row["RendAzon"] is DBNull ? String.Empty : row.Field<string>("RendAzon");
                                DateTime rogzitve = row.Field<DateTime>("Rogzitve");

                                log.Debug($"----- {rogzitve.ToString("yyyy-MM-dd HH:mm:ss")}-{rendAzon}--{CID}-{agentName}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"-- QueryLogger -- Error: {ex}");
            }
        }
    }
}

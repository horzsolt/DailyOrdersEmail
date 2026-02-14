using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.Metrics;

//TODO: refactor this class and have MetricService for each different jobs
namespace OrderEmail.service
{
    public class MetricService
    {
        private readonly Meter meter;
        private readonly Histogram<int> taskExecutionDuration;
        private readonly Histogram<int> dailyOrderSummaryTaskExecutionDuration;
        private readonly Histogram<int> dailyScriptorOrderSummaryTaskExecutionDuration;
        private readonly Histogram<int> weeklyOrderSummaryTaskExecutionDuration;
        private readonly Histogram<int> monthlyOrderSummaryTaskExecutionDuration;
        private int jobExecutionStatus;
        private int dailyOrderSummaryJobExecutionStatus;
        private int dailyScriptorOrderSummaryJobExecutionStatus;
        private int weeklyOrderSummaryJobExecutionStatus;
        private long weeklyOrderLastSuccessTimestamp;
        private long monthlyOrderLastSuccessTimestamp;
        private int monthlyOrderSummaryJobExecutionStatus;
        private int weeklyScriptorOrderSummaryJobExecutionStatus;
        private int orderCount;
        private double orderSum;
        private int daily_orderCount;
        private double daily_orderSum;
        private int weekly_orderCount;
        private int monthly_orderCount;
        private double weekly_orderSum;
        private double monthly_orderSum;
        private readonly ILogger<MetricService> log;

        public int DailyScriptorOrderSummaryJobExecutionStatus
        {
            get
            {
                return dailyScriptorOrderSummaryJobExecutionStatus;
            }
            set
            {
                dailyScriptorOrderSummaryJobExecutionStatus = value;
            }
        }

        public int DailyOrderSummaryJobExecutionStatus
        {
            get
            {
                return dailyOrderSummaryJobExecutionStatus;
            }
            set
            {
                dailyOrderSummaryJobExecutionStatus = value;
            }
        }

        public int WeeklyScriptorOrderSummaryJobExecutionStatus
        {
            get
            {
                return weeklyScriptorOrderSummaryJobExecutionStatus;
            }
            set
            {
                weeklyScriptorOrderSummaryJobExecutionStatus = value;
            }
        }

        public int WeeklyOrderSummaryJobExecutionStatus
        {
            get
            {
                return weeklyOrderSummaryJobExecutionStatus;
            }
            set
            {
                weeklyOrderSummaryJobExecutionStatus = value;
            }
        }

        public void MarkWeeklyOrderSummaryJobSuccess()
        {
            weeklyOrderLastSuccessTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
            log.LogInformation(
                "Weekly order summary job succeeded at {Timestamp}",
                weeklyOrderLastSuccessTimestamp
            );
        }

        public void MarkMonthlyOrderSummaryJobSuccess()
        {
            monthlyOrderLastSuccessTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            log.LogInformation(
                "Monthly order summary job succeeded at {Timestamp}",
                monthlyOrderLastSuccessTimestamp
            );
        }

        public int MonthlyOrderSummaryJobExecutionStatus
        {
            get
            {
                return monthlyOrderSummaryJobExecutionStatus;
            }
            set
            {
                monthlyOrderSummaryJobExecutionStatus = value;
            }
        }

        public int JobExecutionStatus
        {
            get
            {
                return jobExecutionStatus;
            }
            set
            {
                jobExecutionStatus = value;
            }
        }

        public int OrderCount
        {
            get
            {
                return orderCount;
            }
            set
            {
                orderCount = value;
            }
        }

        public double OrderSum
        {
            get
            {
                return orderSum;
            }
            set
            {
                orderSum = value;
            }
        }

        public int DailyOrderCount
        {
            get
            {
                return daily_orderCount;
            }
            set
            {
                daily_orderCount = value;
            }
        }

        public double DailyOrderSum
        {
            get
            {
                return daily_orderSum;
            }
            set
            {
                daily_orderSum = value;
            }
        }

        public int WeeklyOrderCount
        {
            get
            {
                return weekly_orderCount;
            }
            set
            {
                weekly_orderCount = value;
            }
        }

        public int MonthlyOrderCount
        {
            get
            {
                return monthly_orderCount;
            }
            set
            {
                monthly_orderCount = value;
            }
        }

        public double WeeklyOrderSum
        {
            get
            {
                return weekly_orderSum;
            }
            set
            {
                weekly_orderSum = value;
            }
        }

        public double MonthlyOrderSum
        {
            get
            {
                return monthly_orderSum;
            }
            set
            {
                monthly_orderSum = value;
            }
        }

        public MetricService(IMeterFactory meterFactory, ILogger<MetricService> logger, string serviceName, string serviceVersion)
        {
            JobExecutionStatus = 1;
            OrderCount = 0;
            OrderSum = 0;
            DailyOrderCount = 0;
            DailyOrderSum = 0;
            WeeklyOrderCount = 0;
            WeeklyOrderSum = 0;
            MonthlyOrderCount = 0;
            MonthlyOrderSum = 0;
            WeeklyOrderSummaryJobExecutionStatus = 0;
            MonthlyOrderSummaryJobExecutionStatus = 0;
            weeklyOrderLastSuccessTimestamp = 0;
            monthlyOrderLastSuccessTimestamp = 0;

            log = logger;
            meter = meterFactory.Create(serviceName, serviceVersion);

            taskExecutionDuration = meter.CreateHistogram<int>(
              name: "dailyorder_job_execution_duration", unit: "seconds",
              description: "Daily order job execution duration in seconds.");

            dailyOrderSummaryTaskExecutionDuration = meter.CreateHistogram<int>(
              name: "5pm_dailyturnover_job_execution_duration", unit: "seconds",
              description: "5pm daily turnover job execution duration in seconds.");

            weeklyOrderSummaryTaskExecutionDuration = meter.CreateHistogram<int>(
              name: "weeklyturnover_job_execution_duration", unit: "seconds",
              description: "Weekly turnover job execution duration in seconds.");

            monthlyOrderSummaryTaskExecutionDuration = meter.CreateHistogram<int>(
              name: "monthlyturnover_job_execution_duration", unit: "seconds",
              description: "Monthly turnover job execution duration in seconds.");

            dailyScriptorOrderSummaryTaskExecutionDuration = meter.CreateHistogram<int>(
              name: "5pm_daily_scriptor_turnover_job_execution_duration", unit: "seconds",
              description: "5pm daily Scriptor turnover job execution duration in seconds.");


            meter.CreateObservableGauge(
                name: "dailyordersummary_job_execution_status",
                unit: "value",
                observeValue: () => new Measurement<int>(DailyOrderSummaryJobExecutionStatus),
                description:
                "The result code of the latest daily summary order checker job execution (0 = Failed, 1 = Succeeded)"
            );

            meter.CreateObservableGauge(
                name: "weeklyordersummary_job_execution_status",
                unit: "value",
                observeValue: () => new Measurement<int>(WeeklyOrderSummaryJobExecutionStatus),
                description:
                "The result code of the latest weekly summary order checker job execution (0 = Failed, 1 = Succeeded)"
            );

            meter.CreateObservableGauge(
                name: "weeklyorder_last_success_timestamp_seconds",
                unit: "seconds",
                observeValue: () => new Measurement<long>(weeklyOrderLastSuccessTimestamp),
                description: "Unix timestamp (seconds) of the last successful weekly order summary job execution."
            );

            meter.CreateObservableGauge(
                name: "monthlyorder_last_success_timestamp_seconds",
                unit: "seconds",
                observeValue: () => new Measurement<long>(monthlyOrderLastSuccessTimestamp),
                description: "Unix timestamp (seconds) of the last successful monthly order summary job execution."
            );

            meter.CreateObservableGauge(
                name: "monthlyordersummary_job_execution_status",
                unit: "value",
                observeValue: () => new Measurement<int>(MonthlyOrderSummaryJobExecutionStatus),
                description:
                "The result code of the latest monthly summary order checker job execution (0 = Failed, 1 = Succeeded)"
            );

            meter.CreateObservableGauge(
                name: "dailyorderscriptor_summary_job_execution_status",
                unit: "value",
                observeValue: () => new Measurement<int>(DailyScriptorOrderSummaryJobExecutionStatus),
                description:
                "The result code of the latest daily Scriptor order summary checker job execution (0 = Failed, 1 = Succeeded)"
            );

            meter.CreateObservableGauge(
                name: "dailyorder_job_execution_status",
                unit: "value",
                observeValue: () => new Measurement<int>(JobExecutionStatus),
                description:
                "The result code of the latest daily order checker job execution (0 = Failed, 1 = Succeeded)"
            );

            meter.CreateObservableGauge(
                name: "dailyorder_count",
                unit: "orders",
                observeValue: () => new Measurement<int>(OrderCount),
                description: "Daily order count."
            );

            meter.CreateObservableGauge(
                name: "weeklyorder_count",
                unit: "orders",
                observeValue: () => new Measurement<int>(WeeklyOrderCount),
                description: "Weekly order count."
            );

            meter.CreateObservableGauge(
                name: "monthlyorder_count",
                unit: "orders",
                observeValue: () => new Measurement<int>(MonthlyOrderCount),
                description: "Monthly order count."
            );

            meter.CreateObservableGauge(
                name: "dailyorder_sum",
                unit: "money",
                observeValue: () => new Measurement<double>(OrderSum),
                description: "Daily order summary value."
            );

            meter.CreateObservableGauge(
                name: "weeklyorder_sum",
                unit: "money",
                observeValue: () => new Measurement<double>(WeeklyOrderSum),
                description: "Weekly order summary value."
            );

            meter.CreateObservableGauge(
                name: "monthlyorder_sum",
                unit: "money",
                observeValue: () => new Measurement<double>(MonthlyOrderSum),
                description: "Monthly order summary value."
            );

            meter.CreateObservableGauge(
                name: "5pm_dailyorder_count",
                unit: "orders",
                observeValue: () => new Measurement<int>(DailyOrderCount),
                description: "5pm daily order count."
            );

            meter.CreateObservableGauge(
                name: "5pm_dailyorder_sum",
                unit: "money",
                observeValue: () => new Measurement<double>(DailyOrderSum),
                description: "5pm daily order summary value."
            );
        }

        public void RecordJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordJobExecutionDuration {duration}");
            taskExecutionDuration.Record(duration);
        }

        public void RecordDailyOrderSummaryJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordDailyOrderSummaryJobExecutionDuration {duration}");
            dailyOrderSummaryTaskExecutionDuration.Record(duration);
        }

        public void RecordWeeklyOrderSummaryJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordWeeklyOrderSummaryJobExecutionDuration {duration}");
            weeklyOrderSummaryTaskExecutionDuration.Record(duration);
        }

        public void RecordMonthlyOrderSummaryJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordMonthlyOrderSummaryJobExecutionDuration {duration}");
            monthlyOrderSummaryTaskExecutionDuration.Record(duration);
        }

        public void RecordDailyScriptorOrderSummaryJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordDailyScriptorOrderSummaryJobExecutionDuration {duration}");
            dailyScriptorOrderSummaryTaskExecutionDuration.Record(duration);
        }
    }
}

using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace DailyOrdersEmail.services
{
    public class MetricService
    {
        private readonly Meter meter;
        private readonly Histogram<int> taskExecutionDuration;
        private readonly Histogram<int> dailyOrderSummaryTaskExecutionDuration;
        private int jobExecutionStatus;
        private int dailyOrderSummaryJobExecutionStatus;
        private int orderCount;
        private double orderSum;
        private int daily_orderCount;
        private double daily_orderSum;
        private readonly ILogger<MetricService> log;

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

        public MetricService(IMeterFactory meterFactory, ILogger<MetricService> logger, string serviceName, string serviceVersion)
        {
            JobExecutionStatus = 1;
            OrderCount = 0;
            OrderSum = 0;
            DailyOrderCount = 0;
            DailyOrderSum = 0;

            log = logger;
            meter = meterFactory.Create(serviceName, serviceVersion);

            taskExecutionDuration = meter.CreateHistogram<int>(
              name: "dailyorder_job_execution_duration", unit: "seconds",
              description: "Daily order job execution duration in seconds.");

            dailyOrderSummaryTaskExecutionDuration = meter.CreateHistogram<int>(
              name: "5pm_dailyturnover_job_execution_duration", unit: "seconds",
              description: "5pm daily turnover job execution duration in seconds.");

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
                name: "dailyorder_sum",
                unit: "money",
                observeValue: () => new Measurement<double>(OrderSum),
                description: "Daily order summary value."
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
    }
}

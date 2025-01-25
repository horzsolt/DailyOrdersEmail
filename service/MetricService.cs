using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace DailyOrdersEmail.services
{
    public class MetricService
    {
        private readonly Meter meter;
        private readonly Histogram<int> taskExecutionDuration;
        private int jobExecutionStatus;
        private int orderCount;
        private double orderSum;
        private readonly ILogger<MetricService> log;
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

        public MetricService(IMeterFactory meterFactory, ILogger<MetricService> logger, string serviceName, string serviceVersion)
        {
            JobExecutionStatus = 1;
            OrderCount = 0;
            OrderSum = 0;

            log = logger;
            meter = meterFactory.Create(serviceName, serviceVersion);

            taskExecutionDuration = meter.CreateHistogram<int>(
              name: "dailyorder_job_execution_duration", unit: "seconds",
              description: "Daily order job execution duration in seconds.");

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
        }

        public void RecordJobExecutionDuration(int duration)
        {
            log.LogDebug($"RecordJobExecutionDuration {duration}");
            taskExecutionDuration.Record(duration);
        }
    }
}

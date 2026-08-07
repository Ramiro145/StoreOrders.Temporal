using Temporalio.Common;
using Temporalio.Workflows;

namespace StoreOrders.Workflows.Configuration;

public static class ActivityOptionsFactory
{
    public static ActivityOptions CreateDefault()
    {
        return new ActivityOptions
        {
            StartToCloseTimeout = TimeSpan.FromSeconds(30),
            ScheduleToCloseTimeout = TimeSpan.FromMinutes(2),
            RetryPolicy = new RetryPolicy
            {
                InitialInterval = TimeSpan.FromSeconds(1),
                BackoffCoefficient = 2,
                MaximumInterval = TimeSpan.FromSeconds(10),
                MaximumAttempts = 5
            }
        };
    }
}

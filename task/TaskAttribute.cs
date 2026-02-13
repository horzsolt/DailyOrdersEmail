using System;

namespace OrderEmail.task
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class WeeklyOrderSummaryTaskAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class DailyOrderSummaryTaskAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class CheckNewOrderTaskAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class PatikamanTaskAttribute : Attribute { }
}

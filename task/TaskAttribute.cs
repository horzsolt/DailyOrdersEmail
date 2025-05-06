using System;

namespace DailyOrdersEmail.task
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class CheckNewOrderTaskAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class PatikamanTaskAttribute : Attribute { }
}

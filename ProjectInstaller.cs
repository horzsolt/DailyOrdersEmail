using System.ComponentModel;
using System.ServiceProcess;


namespace DailyOrdersEmail
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            ServiceProcessInstaller processInstaller = new ServiceProcessInstaller();
            ServiceInstaller serviceInstaller = new ServiceInstaller();

            // Service will run under the system account
            processInstaller.Account = ServiceAccount.LocalSystem;

            // Set the service name and display name
            serviceInstaller.ServiceName = "VIR_DailyOrderEmailService";
            serviceInstaller.DisplayName = "VIR Napi rendelés értesités";
            serviceInstaller.Description = "A VIR napi rendeles email küldö szolgáltatása.";
            serviceInstaller.StartType = ServiceStartMode.Automatic;

            // Add the installers to the installer collection
            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }
}

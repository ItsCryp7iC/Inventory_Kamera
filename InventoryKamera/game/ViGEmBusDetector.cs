using System;
using Microsoft.Win32;

namespace InventoryKamera.game
{
    internal enum ViGEmBusInstallationStatus
    {
        Installed,
        NotInstalled,
        DetectionFailed
    }

    internal interface IViGEmBusDetector
    {
        ViGEmBusInstallationStatus Detect();
    }

    /// <summary>
    /// Checks the actual ViGEmBus kernel-driver service registration. This does not create a client
    /// or virtual controller; successful backend startup remains the functional readiness check.
    /// </summary>
    internal sealed class ViGEmBusDetector : IViGEmBusDetector
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        internal const string ServiceRegistryPath = @"SYSTEM\CurrentControlSet\Services\ViGEmBus";
        private readonly Func<bool> serviceRegistrationExists;

        internal ViGEmBusDetector()
            : this(ServiceRegistrationExists)
        {
        }

        internal ViGEmBusDetector(Func<bool> serviceRegistrationExists)
        {
            this.serviceRegistrationExists = serviceRegistrationExists ??
                throw new ArgumentNullException(nameof(serviceRegistrationExists));
        }

        public ViGEmBusInstallationStatus Detect()
        {
            try
            {
                return serviceRegistrationExists()
                    ? ViGEmBusInstallationStatus.Installed
                    : ViGEmBusInstallationStatus.NotInstalled;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not determine whether the ViGEmBus driver service is installed.");
                return ViGEmBusInstallationStatus.DetectionFailed;
            }
        }

        private static bool ServiceRegistrationExists()
        {
            using RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using RegistryKey service = localMachine.OpenSubKey(ServiceRegistryPath, writable: false);
            return service != null && service.GetValue("ImagePath") is string imagePath &&
                !string.IsNullOrWhiteSpace(imagePath);
        }
    }
}

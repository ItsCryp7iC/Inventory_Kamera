using System.Configuration;

namespace InventoryKamera.Properties
{
    internal sealed partial class Settings
    {
        [UserScopedSetting]
        [DefaultSettingValue("0")]
        public int ControllerConfirmLayout
        {
            get => (int)this[nameof(ControllerConfirmLayout)];
            set => this[nameof(ControllerConfirmLayout)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("True")]
        public bool UseDefaultControllerConfirmLayout
        {
            get => (bool)this[nameof(UseDefaultControllerConfirmLayout)];
            set => this[nameof(UseDefaultControllerConfirmLayout)] = value;
        }
    }
}
